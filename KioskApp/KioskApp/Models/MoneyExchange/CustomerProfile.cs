using System;

namespace OmniKiosk.Wpf.Models.MoneyExchange
{
    public sealed class CustomerProfile
    {
        public long Id { get; set; }

        public string IdType { get; set; } = "";      // Passport/IC
        public string IdNo { get; set; } = "";
        public string FullName { get; set; } = "";
        public string Nationality { get; set; } = "";
        public string Sex { get; set; } = "";
        public string DateOfBirth { get; set; } = ""; // keep string from SDK for now
        public string? DateOfExpiry { get; set; } // passport only - not persisted to DB, re-checked fresh every scan
        public string? DateOfIssue { get; set; }  // passport only - not persisted to DB, keep string from SDK for now
        public string? PlaceOfBirth { get; set; } // passport only - not persisted to DB
        public string? PlaceOfIssue { get; set; } // passport only - not persisted to DB

        // From MyKad's optical recognition (Sinosecu MAINID 2001, field
        // index 6) or the passport chip (index 11, when present) - see
        // PassportReaderService's MyKadOpticalFields/PassportDoc.Address
        // remarks for the confidence caveats on each source. Not currently
        // persisted to SenderMaster (no column for it) - captured here for
        // potential future use, not wired to storage yet.
        public string? Address { get; set; }
        public string MobileNo { get; set; } = "";

        // Face
        public string? FaceFeatureBase64 { get; set; }
        public string? FaceImageBase64 { get; set; }

        // eKYC - full document image (passport only currently; MyKad's
        // chip reader has no optical scan capability, see
        // EkycFaceMatchClient.VerifyPassportAuthenticityAsync remarks).
        // Note: the JourneyId itself lives on MoneyExchangeFlowState, not
        // here - it's flow-scoped, not a property of the customer record.
        public string? IdDocumentImageBase64 { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? LastSeenUtc { get; set; }
    }
}