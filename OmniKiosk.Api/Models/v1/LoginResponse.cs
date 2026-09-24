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

        // Only populated for a kiosk login (KioskLogin below) - null for a
        // staff login, since a staff member isn't tied to one physical
        // terminal. Also baked into the JWT itself as claims (see
        // IssueToken) so anything reading the token later doesn't need to
        // separately trust these response fields - but they're included
        // here too since the kiosk app needs them immediately at startup,
        // before it's made any other authenticated call.
        public string? KioskId { get; set; }
        public int? BranchId { get; set; }
    }
}