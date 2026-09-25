using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using OmniKiosk.Wpf.Config;

namespace OmniKiosk.Wpf.Services.MoneyExchange
{
    public class ApiCurrency
    {
        public string CurrencyCode { get; set; } = "";
        public string CurrencyName { get; set; } = "";
        public string? FlagCountryCode { get; set; }
        public decimal BuyRate { get; set; }
        public decimal SellRate { get; set; }
    }

    public class ApiCountryDialCode
    {
        public string IsoCode { get; set; } = "";
        public string ta3 { get; set; } = "";

        public string CountryName { get; set; } = "";
        public string DialCode { get; set; } = "";
    }

    public class ApiDenomination
    {
        public string CurrencyCode { get; set; } = "";
        public int DenominationValue { get; set; }
    }

    public class ApiDenominationBreakdownLine
    {
        public int DenominationValue { get; set; }
        public int NoteCount { get; set; }
    }

    public class ApiDenominationBreakdown
    {
        public List<ApiDenominationBreakdownLine> Breakdown { get; set; } = new();
        public decimal UnfulfilledAmount { get; set; }
        public bool CanDispenseFully => UnfulfilledAmount == 0;
    }

    public class ApiLimitCheckResult
    {
        public bool IsWithinLimits { get; set; }
        public string? BreachedLimit { get; set; }   // PerTransaction | Daily | Rolling30Day | null
        public decimal DailyTotalSoFar { get; set; }
        public decimal Rolling30DayTotal { get; set; }
    }

    public class ApiPerTransactionLimitResult
    {
        public bool IsWithinLimits { get; set; }
        public decimal PerTxnLimit { get; set; }
    }

    public class ApiPerTxnLimitCheckResult
    {
        public bool IsWithinLimit { get; set; }
        public decimal PerTxnLimit { get; set; }
    }

    public class CreateTransactionApiRequest
    {
        public string KioskId { get; set; } = "";
        public int BranchId { get; set; }
        public int? CustomerRef { get; set; }
        public string FromCurrency { get; set; } = "";
        public decimal FromAmount { get; set; }
        public decimal Rate { get; set; }
        public decimal MyrAmount { get; set; }
        public decimal CashInsertedMyr { get; set; }
        public string CreatedBy { get; set; } = "";
        public string? ScreeningTransGuid { get; set; }
    }

    public class CustomerCheckResult
    {
        public bool Found { get; set; }
        public int? SenderId { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Nationality { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string? MobileNo { get; set; }
        public int? Status { get; set; }
        public bool? IsBlocked { get; set; }
        public decimal? KycScore { get; set; }
        public decimal? MEKycScore { get; set; }
    }

    public class ScreeningResult
    {
        public bool HasMatch { get; set; }
    }

    public class CreateCustomerApiRequest
    {
        public string KioskId { get; set; } = "";
        public int BranchId { get; set; }
        public string IdType { get; set; } = "";
        public string IdNo { get; set; } = "";
        public string FullName { get; set; } = "";
        public string? Nationality { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string? Gender { get; set; }
        public string? MobileNo { get; set; }
        public DateTime? IdExpiryDate { get; set; }
        public string? Picture1Base64 { get; set; }
        public string? IdDocumentImageBase64 { get; set; }   // full document scan - passport only currently, see KSK_CreateNewSender.sql
    }

    // Talks to OmniKiosk.MoneyExchange.Api. Every call now attaches a
    // Bearer token obtained from KioskAuthService (the kiosk's own machine
    // identity, logged in against the same Config.Api /Auth/login endpoint
    // a staff member would use). On a 401, invalidates the cached token and
    // retries exactly once - covers the case where the token expired between
    // KioskAuthService handing it out and this request actually landing.
    public sealed class MoneyExchangeApiClient
    {
        private static readonly HttpClient _http = new HttpClient
        {
            BaseAddress = new Uri(KioskSettings.MoneyExchangeApiBaseUrl),
            Timeout = TimeSpan.FromSeconds(15)
        };

        private static async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> buildRequest, CancellationToken ct)
        {
            var token = await KioskAuthService.GetTokenAsync(ct);
            var request = buildRequest();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var response = await _http.SendAsync(request, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                KioskAuthService.InvalidateToken();
                var retryToken = await KioskAuthService.GetTokenAsync(ct);
                var retryRequest = buildRequest();
                retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", retryToken);
                response = await _http.SendAsync(retryRequest, ct);
            }

            return response;
        }

        public async Task<List<ApiCurrency>> GetCurrenciesAsync(CancellationToken ct = default)
        {
            var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, "api/v1/Currencies"), ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<List<ApiCurrency>>(cancellationToken: ct);
            return result ?? new List<ApiCurrency>();
        }

        // KSK_GetCountryDialCodes, Malaysia sorted first. Pure reference
        // data - a failure here shouldn't block the customer details step
        // over a lookup that's just for display convenience, so this
        // returns an empty list rather than throwing; the caller falls
        // back to a plain +60 default if the list comes back empty.
        public async Task<List<ApiCountryDialCode>> GetDialCodesAsync(CancellationToken ct = default)
        {
            try
            {
                var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, "api/v1/Currencies/dial-codes"), ct);
                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadFromJsonAsync<List<ApiCountryDialCode>>(cancellationToken: ct);
                return result ?? new List<ApiCountryDialCode>();
            }
            catch
            {
                return new List<ApiCountryDialCode>();
            }
        }

        // KSK_GetDenominations - the real, existing Ksk_BanknoteDenominations
        // table, filtered to IsEnabled=1 for this currency.
        public async Task<List<ApiDenomination>> GetDenominationsAsync(string currencyCode, CancellationToken ct = default)
        {
            var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, $"api/v1/Currencies/denominations/{currencyCode}"), ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<List<ApiDenomination>>(cancellationToken: ct);
            return result ?? new List<ApiDenomination>();
        }

        // Pre-dispense availability check - KSK_GetDenominationBreakdown.
        // kioskId should be the real, server-resolved identifier from
        // KioskAuthService.GetKioskIdAsync() (Ksk_Terminals.KioskId,
        // format "K-00001" etc, matched against Ksk_CashInventory) - not a
        // client-side literal. FinalReceiptStep now treats a genuine
        // CanDispenseFully=false result as a real hard-stop.
        public async Task<ApiDenominationBreakdown> GetDenominationBreakdownAsync(string kioskId, decimal targetAmount, CancellationToken ct = default)
        {
            var url = $"api/v1/Currencies/denomination-breakdown?kioskId={Uri.EscapeDataString(kioskId)}&targetAmount={targetAmount}";
            var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<ApiDenominationBreakdown>(cancellationToken: ct);
            return result ?? new ApiDenominationBreakdown();
        }

        // KSK_CheckPerTransactionLimitOnly - ID-agnostic, for use before any
        // customer has been identified (currency selection screen). Fail-
        // closed on any failure, same reasoning as CheckLimitsAsync below -
        // this is a compliance control, not a convenience check.
        public async Task<ApiPerTransactionLimitResult> CheckPerTransactionLimitAsync(string kioskId, decimal proposedMyrAmount, CancellationToken ct = default)
        {
            var url = $"api/v1/Transactions/check-per-transaction-limit?kioskId={Uri.EscapeDataString(kioskId)}&proposedMyrAmount={proposedMyrAmount}";
            var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<ApiPerTransactionLimitResult>(cancellationToken: ct);
            return result ?? throw new InvalidOperationException("Per-transaction limit check returned an empty response.");
        }

        // KSK_CheckMoneyExchangeLimits - per-transaction, daily, and
        // rolling-30-day limits. Matched on SenderId directly, per
        // instruction - does not resolve across every SenderMaster row
        // sharing the same IdNo (see the proc's own header comment for
        // what that means for a customer with duplicate sender records).
        //
        // Throws on failure rather than returning a fail-open default -
        // deliberately the OPPOSITE choice from GetDenominationBreakdownAsync
        // above. That check protects against a denial-of-service (blocking
        // a legitimate customer over a hardware/network hiccup); this one
        // protects a regulatory compliance control (BNM daily/monthly
        // limits). An unreachable compliance check should stop the
        // transaction, not silently let it through - the caller (CashInStep)
        // treats any exception from this method as "cannot verify, block
        // and direct to the counter", not "proceed anyway".
        public async Task<ApiLimitCheckResult> CheckLimitsAsync(int senderId, string kioskId, decimal proposedMyrAmount, CancellationToken ct = default)
        {
            var url = $"api/v1/Transactions/check-limits?senderId={senderId}&kioskId={Uri.EscapeDataString(kioskId)}&proposedMyrAmount={proposedMyrAmount}";
            var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<ApiLimitCheckResult>(cancellationToken: ct);
            return result ?? throw new InvalidOperationException("Limit check returned an empty response - cannot confirm whether limits are within range.");
        }

        // KSK_CheckPerTransactionLimitOnly - for CurrencySelectionStep,
        // before any customer identity is known. Same fail-CLOSED posture
        // as CheckLimitsAsync above and for the same reason (a regulatory
        // control, not a convenience check) - throws rather than assuming
        // "within limit" on an empty/unreachable response.
        public async Task<ApiPerTxnLimitCheckResult> CheckPerTransactionLimitOnlyAsync(string kioskId, decimal proposedMyrAmount, CancellationToken ct = default)
        {
            var url = $"api/v1/Transactions/check-per-txn-limit?kioskId={Uri.EscapeDataString(kioskId)}&proposedMyrAmount={proposedMyrAmount}";
            var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<ApiPerTxnLimitCheckResult>(cancellationToken: ct);
            return result ?? throw new InvalidOperationException("Per-transaction limit check returned an empty response - cannot confirm whether the limit is within range.");
        }

        public async Task<CustomerCheckResult> CheckCustomerAsync(string idType, string idNo, CancellationToken ct = default)
        {
            var url = $"api/v1/Customers/check?idType={Uri.EscapeDataString(idType)}&idNo={Uri.EscapeDataString(idNo)}";
            var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<CustomerCheckResult>(cancellationToken: ct);
            return result ?? new CustomerCheckResult { Found = false };
        }

        // Only call this after CheckCustomerAsync comes back Found=false -
        // creates a new SenderMaster record and returns its real SenderID.
        public async Task<int> CreateCustomerAsync(CreateCustomerApiRequest request, CancellationToken ct = default)
        {
            var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, "api/v1/Customers") { Content = JsonContent.Create(request) }, ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<CreateCustomerResult>(cancellationToken: ct);
            return result?.SenderId ?? 0;
        }

        // Watchlist screening - call this as early as possible once a
        // SenderId is known, before any cash moves. HasMatch=true means the
        // kiosk flow must stop, not continue with a "we'll sort it out later"
        // attitude. transGuid is the flow-level GUID generated once by
        // MoneyExchangeFlowController at construction, not created here.
        public async Task<ScreeningResult> ScreenCustomerAsync(int senderId, string transGuid, CancellationToken ct = default)
        {
            var url = $"api/v1/Customers/{senderId}/screen?transGuid={Uri.EscapeDataString(transGuid)}";
            var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, url), ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<ScreeningResult>(cancellationToken: ct);
            return result ?? new ScreeningResult { HasMatch = false };
        }

        public async Task<(long TransactionId, string ReceiptNo)> CreateTransactionAsync(CreateTransactionApiRequest request, CancellationToken ct = default)
        {
            var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, "api/v1/Transactions") { Content = JsonContent.Create(request) }, ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<CreateTransactionResult>(cancellationToken: ct);
            return (result?.TransactionId ?? 0, result?.ReceiptNo ?? "");
        }

        // Call this once cash-in is done, before CompleteTransactionAsync -
        // pushes the final running totals to the transaction record.
        // KSK_MirrorToMcTransaction reads these same columns later, so
        // skipping this call means the Mc_ mirror runs against 0 values.
        //public async Task UpdateTransactionAmountsAsync(long transactionId, decimal fromAmount, decimal myrAmount, decimal cashInsertedMyr, CancellationToken ct = default)
        //{
        //    var body = new { FromAmount = fromAmount, MyrAmount = myrAmount, CashInsertedMyr = cashInsertedMyr };
        //    var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Put, $"api/v1/Transactions/{transactionId}/amounts") { Content = JsonContent.Create(body) }, ct);
        //    response.EnsureSuccessStatusCode();
        //}

        //public async Task CompleteTransactionAsync(long transactionId, string status, CancellationToken ct = default)
        //{
        //    var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Put, $"api/v1/Transactions/{transactionId}/complete") { Content = JsonContent.Create(new { Status = status }) }, ct);
        //    response.EnsureSuccessStatusCode();
        //}
        public async Task UpdateTransactionAmountsAsync(
    long transactionId,
    decimal fromAmount,
    decimal myrAmount,
    decimal cashInsertedMyr,
    CancellationToken ct = default)
        {
            var body = new
            {
                FromAmount = fromAmount,
                MyrAmount = myrAmount,
                CashInsertedMyr = cashInsertedMyr
            };

            var response = await SendAsync(
                () => new HttpRequestMessage(
                    HttpMethod.Post,
                    $"api/v1/Transactions/{transactionId}/amounts")
                {
                    Content = JsonContent.Create(body)
                },
                ct);

            response.EnsureSuccessStatusCode();
        }

        public async Task CompleteTransactionAsync(
            long transactionId,
            string status,
            CancellationToken ct = default)
        {
            var response = await SendAsync(
                () => new HttpRequestMessage(
                    HttpMethod.Post,
                    $"api/v1/Transactions/{transactionId}/complete")
                {
                    Content = JsonContent.Create(new { Status = status })
                },
                ct);

            response.EnsureSuccessStatusCode();
        }
        public async Task RecordNoteAsync(long transactionId, int sequenceNo, string currencyCode, decimal denominationValue, string outcome, CancellationToken ct = default)
        {
            var body = new { SequenceNo = sequenceNo, CurrencyCode = currencyCode, DenominationValue = denominationValue, Outcome = outcome };
            var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, $"api/v1/Transactions/{transactionId}/notes") { Content = JsonContent.Create(body) }, ct);
            response.EnsureSuccessStatusCode();
        }

        // Full-journey audit trail. Deliberately swallows every exception -
        // matches KSK_LogJourneyEvent's own "never throws" posture. Journey
        // logging must never be able to interrupt a real transaction.
        public async Task LogJourneyEventAsync(
            Guid sessionId, string serviceType, string eventType, string stepName,
            int? branchId = null, string? kioskLoginId = null, string? outcome = null,
            string? details = null, long? transactionId = null, CancellationToken ct = default)
        {
            try
            {
                var body = new
                {
                    SessionId = sessionId,
                    ServiceType = serviceType,
                    BranchId = branchId,
                    KioskLoginId = kioskLoginId,
                    EventType = eventType,
                    StepName = stepName,
                    Outcome = outcome,
                    Details = details,
                    TransactionId = transactionId
                };
                var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, "api/v1/JourneyEvents") { Content = JsonContent.Create(body) }, ct);
                if (!response.IsSuccessStatusCode)
                    KioskLocalLogger.LogError("JourneyEvents", $"LogJourneyEventAsync got HTTP {(int)response.StatusCode} for {eventType}/{stepName}");
            }
            catch (Exception ex)
            {
                KioskLocalLogger.LogError("JourneyEvents", $"LogJourneyEventAsync failed for {eventType}/{stepName}: {ex.Message}");
            }
        }

        private class CreateTransactionResult
        {
            public long TransactionId { get; set; }
            public string ReceiptNo { get; set; } = "";
        }

        private class CreateCustomerResult
        {
            public int SenderId { get; set; }
        }
    }
}