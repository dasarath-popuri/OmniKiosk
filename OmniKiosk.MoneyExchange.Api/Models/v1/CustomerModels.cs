namespace OmniKiosk.MoneyExchange.Api.Models.v1
{
    public class CustomerCheckResult
    {
        public bool Found { get; set; }
        public int? SenderId { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Nationality { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string? MobileNo { get; set; }
        public int? Status { get; set; }
        public bool? IsBlocked { get; set; }
        public decimal? KycScore { get; set; }
        public decimal? MEKycScore { get; set; }
    }
}
