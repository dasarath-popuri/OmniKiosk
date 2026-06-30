using System;

namespace OmniKiosk.Wpf.Models.MoneyExchange
{
    public sealed class MoneyExchangeTxn
    {
        public long Id { get; set; }
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public long CustomerId { get; set; }

        public string FromCurrency { get; set; } = "";
        public double FromAmount { get; set; }
        public double Rate { get; set; }
        public double MyrAmount { get; set; }

        public double CashInsertedMyr { get; set; }
        public string Status { get; set; } = "Created"; // Created, PaidIn, Dispensed, Completed, Failed
        public string? Notes { get; set; }

    }
}
