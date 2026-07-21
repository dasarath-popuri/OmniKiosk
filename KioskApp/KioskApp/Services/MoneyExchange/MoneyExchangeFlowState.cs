using OmniKiosk.Wpf.Models.MoneyExchange;

namespace OmniKiosk.Wpf.Services.MoneyExchange
{
    public sealed class MoneyExchangeFlowState
    {
        // Quote
        public string FromCurrency { get; set; } = "USD";
        public double FromAmount { get; set; }
        public double RateToMyr { get; set; }
        public double MyrAmount { get; set; }

        // Customer
        public CustomerProfile? Customer { get; set; }

        // Set by MoneyExchangeFlowController.UpsertCustomer - true if this
        // IdType+IdNo was already in the local database before this visit.
        // Drives FaceVerificationStep: existing customers get the fast local
        // TaiSDK match, new customers go through Innov8tif eKYC.
        public bool IsExistingCustomer { get; set; }

        // Face
        public bool FaceVerified { get; set; }

        // Cash-in
        public double CashInsertedMyr { get; set; }

        // Transaction
        public long? TransactionId { get; set; }

        // Add this inside OmniKiosk.Wpf.Models.MoneyExchange.MoneyExchangeState
        public System.Collections.ObjectModel.ObservableCollection<OmniKiosk.Wpf.Views.MoneyExchange.Steps.TransactionItem> Transactions { get; set; } = new();
        public string? LiveFaceImageBase64 { get; set; } // To store the live face for the final receipt

    }
}
