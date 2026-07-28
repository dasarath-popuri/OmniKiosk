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

        // Resolved from SenderMaster via CustomersController.CheckCustomer -
        // null until CustomerDetailsStep completes the check, then set if a
        // match was found. Sent as CustomerRef when creating the transaction.
        public int? SenderId { get; set; }

        // Generated once by MoneyExchangeFlowController's constructor, right
        // at the start of the flow - not at screening time. Sent to
        // KSK_TransScreening (CustomerDetailsStep) and later to
        // KSK_CommitScreening (at completion, once a real Mc_TransMaster.TxnID
        // exists) - the same identifier ties the two together.
        public string? ScreeningTransGuid { get; set; }

        // Authoritative source is now CustomersController.CheckCustomer
        // against SenderMaster (set in CustomerDetailsStep), not the local
        // SQLite cache - the local upsert still happens for its own reason
        // (caching a face-match feature for fast local re-verification), but
        // the central check is what determines existing-vs-new at the
        // business level, and drives FaceVerificationStep's local-match vs
        // eKYC branch.
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
