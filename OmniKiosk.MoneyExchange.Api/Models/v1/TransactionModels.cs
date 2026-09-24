namespace OmniKiosk.MoneyExchange.Api.Models.v1
{
    public class CurrencyDto
    {
        public string CurrencyCode { get; set; } = "";
        public string CurrencyName { get; set; } = "";
        public string? FlagCountryCode { get; set; }
        public decimal BuyRate { get; set; }
        public decimal SellRate { get; set; }
    }

    public class DenominationDto
    {
        public string CurrencyCode { get; set; } = "";
        public int DenominationValue { get; set; }
    }

    public class CountryDialCodeDto
    {
        public string IsoCode { get; set; } = "";
        public string CountryName { get; set; } = "";
        public string DialCode { get; set; } = "";

        public string ta3 { get; set; }
    }

    // For KSK_GetDenominationBreakdown - the pre-dispense availability
    // check. The proc returns two result sets: the breakdown rows, then a
    // single UnfulfilledAmount row (>0 means the kiosk's current cassette
    // mix cannot fully cover the requested amount).
    public class DenominationBreakdownLineDto
    {
        public int DenominationValue { get; set; }
        public int NoteCount { get; set; }
    }

    public class DenominationBreakdownResponseDto
    {
        public List<DenominationBreakdownLineDto> Breakdown { get; set; } = new();
        public decimal UnfulfilledAmount { get; set; }
        public bool CanDispenseFully => UnfulfilledAmount == 0;
    }

    public class CreateTransactionRequest
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

    public class CreateTransactionResponse
    {
        public long TransactionId { get; set; }
        public string ReceiptNo { get; set; } = "";
    }

    public class CompleteTransactionRequest
    {
        // InProgress, Completed, Cancelled, DispenseFailed
        public string Status { get; set; } = "";
    }

    public class UpdateAmountsRequest
    {
        public decimal FromAmount { get; set; }
        public decimal MyrAmount { get; set; }
        public decimal CashInsertedMyr { get; set; }
    }

    public class RecordNoteRequest
    {
        public int SequenceNo { get; set; }
        public string CurrencyCode { get; set; } = "";
        public decimal DenominationValue { get; set; }
        public string Outcome { get; set; } = ""; // Accepted, Returned, Rejected
    }

    public class LogJourneyEventRequest
    {
        public Guid SessionId { get; set; }
        public string ServiceType { get; set; } = "MoneyExchange";
        public int? BranchId { get; set; }
        public string? KioskLoginId { get; set; }
        public string EventType { get; set; } = "";   // SessionStart | StepEntered | StepCompleted | StepAbandoned | StepFailed | SessionEnd
        public string StepName { get; set; } = "";
        public string? Outcome { get; set; }           // Success | Failure | Error | null
        public string? Details { get; set; }
        public long? TransactionId { get; set; }
    }

    // For KSK_CheckPerTransactionLimitOnly.
    public class CheckPerTransactionLimitResponse
    {
        public bool IsWithinLimits { get; set; }
        public decimal PerTxnLimit { get; set; }
    }

    // For KSK_CheckMoneyExchangeLimits.
    public class CheckLimitsResponse
    {
        public bool IsWithinLimits { get; set; }
        public string? BreachedLimit { get; set; }   // PerTransaction | Daily | Rolling30Day | null
        public decimal DailyTotalSoFar { get; set; }
        public decimal Rolling30DayTotal { get; set; }
    }

    // For KSK_CheckPerTransactionLimitOnly - the narrower, identity-free
    // check used at CurrencySelectionStep.
    public class CheckPerTxnLimitResponse
    {
        public bool IsWithinLimit { get; set; }
        public decimal PerTxnLimit { get; set; }
    }
}