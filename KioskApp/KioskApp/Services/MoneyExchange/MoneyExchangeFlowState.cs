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

        // Declared at CurrencySelectionStep, BEFORE any cash is physically
        // inserted - used only for the early limit pre-checks (per-
        // transaction at declaration time, full daily/monthly once SenderId
        // is known in CustomerDetailsStep). Deliberately separate from
        // FromAmount/MyrAmount above, which get overwritten with the REAL
        // accumulated totals as CashInStep actually runs - a customer's
        // final inserted amount can differ from what they declared here,
        // and CashInStep's own checks (built separately) are what actually
        // govern the real transaction.
        public double IntendedFromAmount { get; set; }
        public double IntendedMyrAmount { get; set; }

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

        // Full-journey audit trail (Ksk_KioskJourneyEvents) - one per flow
        // instance, generated alongside ScreeningTransGuid above. Separate
        // identifier because journey events exist independently of whether
        // screening (or any transaction at all) ever happens.
        public Guid SessionId { get; set; } = Guid.NewGuid();

        // eKYC - one JourneyId reused across OkayID, OkayDoc, OkayFace,
        // OkayLive, and Scorecard for the whole flow, per Innov8tif's own
        // guidance ("the JourneyId should be used for the rest of one eKYC
        // flow"). Set once by whichever step runs first for a new customer
        // (normally CustomerDetailsStep's document read); every later step
        // must check this before creating its own.
        public string? EkycJourneyId { get; set; }

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

        // The real, sequential receipt number from KSK_GetReceiptNo,
        // returned when CashInStep creates the transaction. Replaces the
        // old client-side ReceiptFormatter.BuildReceiptNo generation -
        // FinalReceiptStep's printed receipt should use this, not generate
        // its own.
        public string? ReceiptNo { get; set; }

        // Add this inside OmniKiosk.Wpf.Models.MoneyExchange.MoneyExchangeState
        public System.Collections.ObjectModel.ObservableCollection<OmniKiosk.Wpf.Views.MoneyExchange.Steps.TransactionItem> Transactions { get; set; } = new();
        public string? LiveFaceImageBase64 { get; set; } // To store the live face for the final receipt

        public int LastTransactionNoteSequence { get; set; }
    }
}