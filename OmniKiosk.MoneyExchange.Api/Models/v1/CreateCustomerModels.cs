namespace OmniKiosk.MoneyExchange.Api.Models.v1
{
    public class CreateCustomerRequest
    {
        public string KioskId { get; set; } = "";
        public string IdType { get; set; } = ""; // "IC" or "Passport"
        public string IdNo { get; set; } = "";
        public string FullName { get; set; } = "";
        public string? Nationality { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string? Gender { get; set; }
        public string? MobileNo { get; set; }
        public DateTime? IdExpiryDate { get; set; }
    }

    public class CreateCustomerResponse
    {
        public int SenderId { get; set; }
    }
}
