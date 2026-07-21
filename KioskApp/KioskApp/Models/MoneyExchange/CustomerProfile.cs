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
        public string MobileNo { get; set; } = "";

        // Face
        public string? FaceFeatureBase64 { get; set; }
        public string? FaceImageBase64 { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? LastSeenUtc { get; set; }
    }
}
