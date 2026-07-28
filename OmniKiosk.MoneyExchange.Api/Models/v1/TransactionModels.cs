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

    public class CreateTransactionRequest
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

    public class CreateTransactionResponse
    {
        public long TransactionId { get; set; }
    }

    public class CompleteTransactionRequest
    {
        // InProgress, Completed, Cancelled, DispenseFailed
        public string Status { get; set; } = "";
    }

    public class RecordNoteRequest
    {
        public int SequenceNo { get; set; }
        public string CurrencyCode { get; set; } = "";
        public decimal DenominationValue { get; set; }
        public string Outcome { get; set; } = ""; // Accepted, Returned, Rejected
    }
}
