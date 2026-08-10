namespace OmniKiosk.MoneyExchange.Api.Models.v1
{
    public class CreateCustomerRequest
    {
        public string KioskId { get; set; } = "";
        public int BranchId { get; set; }
        public string IdType { get; set; } = ""; // "IC" or "Passport"
        public string IdNo { get; set; } = "";
        public string FullName { get; set; } = "";
        public string? Nationality { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string? Gender { get; set; }
        public string? MobileNo { get; set; }
        public DateTime? IdExpiryDate { get; set; }
        // Base64 over the wire (JSON has no binary type) - decoded back to
        // raw bytes server-side before going into SenderMaster.Picture1
        // (varbinary(max)), not stored as base64 text.
        public string? Picture1Base64 { get; set; }
    }

    public class CreateCustomerResponse
    {
        public int SenderId { get; set; }
    }
}