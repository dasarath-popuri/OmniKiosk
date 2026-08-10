namespace OmniKiosk.Config.Api.Models.v1
{
    public class LoginResponse
    {
        public string Token { get; set; } = "";
        public DateTime ExpiresAtUtc { get; set; }
        public string FullName { get; set; } = "";
        public string Role { get; set; } = "";
        public int UserId { get; set; }
        public string UserCode { get; set; } = "";
    }
}
