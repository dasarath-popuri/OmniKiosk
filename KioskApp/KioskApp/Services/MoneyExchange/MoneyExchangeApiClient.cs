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

    public class ApiDenomination
    {
        public string CurrencyCode { get; set; } = "";
        public int DenominationValue { get; set; }
    }

    public class CreateTransactionApiRequest
    {
        public string KioskId { get; set; } = "";
        public string ReceiptNo { get; set; } = "";
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
        public string IdType { get; set; } = "";
        public string IdNo { get; set; } = "";
        public string FullName { get; set; } = "";
        public string? Nationality { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string? Gender { get; set; }
        public string? MobileNo { get; set; }
        public DateTime? IdExpiryDate { get; set; }
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

        public async Task<List<ApiDenomination>> GetDenominationsAsync(string currencyCode, CancellationToken ct = default)
        {
            var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, $"api/v1/Currencies/denominations/{currencyCode}"), ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<List<ApiDenomination>>(cancellationToken: ct);
            return result ?? new List<ApiDenomination>();
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

        public async Task<long> CreateTransactionAsync(CreateTransactionApiRequest request, CancellationToken ct = default)
        {
            var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, "api/v1/Transactions") { Content = JsonContent.Create(request) }, ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<CreateTransactionResult>(cancellationToken: ct);
            return result?.TransactionId ?? 0;
        }

        public async Task CompleteTransactionAsync(long transactionId, string status, CancellationToken ct = default)
        {
            var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Put, $"api/v1/Transactions/{transactionId}/complete") { Content = JsonContent.Create(new { Status = status }) }, ct);
            response.EnsureSuccessStatusCode();
        }

        public async Task RecordNoteAsync(long transactionId, int sequenceNo, string currencyCode, decimal denominationValue, string outcome, CancellationToken ct = default)
        {
            var body = new { SequenceNo = sequenceNo, CurrencyCode = currencyCode, DenominationValue = denominationValue, Outcome = outcome };
            var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, $"api/v1/Transactions/{transactionId}/notes") { Content = JsonContent.Create(body) }, ct);
            response.EnsureSuccessStatusCode();
        }

        private class CreateTransactionResult
        {
            public long TransactionId { get; set; }
        }

        private class CreateCustomerResult
        {
            public int SenderId { get; set; }
        }
    }
}
