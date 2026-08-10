using System.Net.Http;
using System.Net.Http.Json;
using OmniKiosk.Wpf.Config;

namespace OmniKiosk.Wpf.Services
{
    // Machine-level login for this physical kiosk, not a human. Calls the
    // SAME /api/v1/Auth/login endpoint a staff member would use - the
    // difference is purely which LoginId is sent, and which role that
    // LoginId resolves to server-side (RoleName='KIOSK' -> MachineType=
    // "Kiosk" claim in the JWT). One token, reused across every API call
    // this kiosk makes (Config.Api, MoneyExchange.Api, and Remittance.Api
    // once that exists), refreshed automatically before it expires.
    public static class KioskAuthService
    {
        private static readonly HttpClient _http = new HttpClient
        {
            BaseAddress = new Uri(KioskSettings.ConfigApiBaseUrl),
            Timeout = TimeSpan.FromSeconds(15)
        };

        private static string? _token;
        private static int _userId;
        private static string? _userCode;
        private static DateTime _expiresAtUtc = DateTime.MinValue;
        private static readonly SemaphoreSlim _lock = new(1, 1);

        // Returns a valid token, logging in (or re-logging in) if the
        // cached one is missing or close to expiry. Safe to call from
        // multiple places concurrently - only one actual login request
        // happens even if several callers ask at the same moment.
        public static async Task<string> GetTokenAsync(CancellationToken ct = default)
        {
            await EnsureLoggedInAsync(ct);
            return _token!;
        }

        // The kiosk's own real UserID from UserProfile - use this for any
        // CreatedBy-type field, never a placeholder string like "KIOSK".
        // Ksk_MoneyExchangeTransaction.CreatedBy is a varchar and would
        // accept anything, but KSK_MirrorToMcTransaction later casts it to
        // int for the shared Mc_TransMaster/Mc_Transaction tables - a
        // non-numeric value there fails that cast silently and the whole
        // mirror insert never happens.
        public static async Task<int> GetKioskUserIdAsync(CancellationToken ct = default)
        {
            await EnsureLoggedInAsync(ct);
            return _userId;
        }

        // BranchId comes from this kiosk's own UserProfile.UserCode, per
        // the actual data model (not the separate Ksk_Terminals.BranchId
        // lookup used before, which required its own provisioning step and
        // was never confirmed populated). Throws rather than silently
        // falling back to 0 if UserCode isn't numeric - a transaction
        // attributed to the wrong branch is worse than one that fails
        // loudly at login and gets noticed immediately.
        //
        // *** Worth double-checking against the DB: earlier guidance in
        // this build had UserCode set equal to LoginId (a string like
        // "KIOSK-K101") for the kiosk's own UserProfile row. If that's
        // still the case, this will throw on every login. UserCode needs
        // to actually hold the numeric BranchId for this to work. ***
        public static async Task<int> GetKioskBranchIdAsync(CancellationToken ct = default)
        {
            await EnsureLoggedInAsync(ct);
            if (!int.TryParse(_userCode, out var branchId))
                throw new InvalidOperationException(
                    $"UserProfile.UserCode ('{_userCode}') for this kiosk is not a valid numeric BranchId. " +
                    "Check the kiosk's UserProfile record - UserCode must hold the branch ID, not the LoginId or anything else.");
            return branchId;
        }

        private static async Task EnsureLoggedInAsync(CancellationToken ct)
        {
            // 60-second buffer so a token that's about to expire mid-request
            // doesn't get handed out and then rejected a moment later.
            if (_token != null && DateTime.UtcNow < _expiresAtUtc.AddSeconds(-60))
                return;

            await _lock.WaitAsync(ct);
            try
            {
                // Re-check after acquiring the lock - another caller may have
                // already refreshed it while this one was waiting.
                if (_token != null && DateTime.UtcNow < _expiresAtUtc.AddSeconds(-60))
                    return;

                var response = await _http.PostAsJsonAsync("api/v1/Auth/login", new
                {
                    LoginId = KioskSettings.KioskLoginId,
                    Password = KioskSettings.KioskLoginPassword
                }, ct);

                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadFromJsonAsync<KioskLoginResponse>(cancellationToken: ct)
                    ?? throw new InvalidOperationException("Kiosk login returned an empty response.");

                _token = result.Token;
                _userId = result.UserId;
                _userCode = result.UserCode;
                _expiresAtUtc = result.ExpiresAtUtc;
            }
            finally
            {
                _lock.Release();
            }
        }

        // Call this if a downstream API call comes back 401 mid-session -
        // forces the next GetTokenAsync to re-login rather than hand out
        // the same (apparently now-rejected) cached token again.
        public static void InvalidateToken()
        {
            _token = null;
            _userId = 0;
            _userCode = null;
            _expiresAtUtc = DateTime.MinValue;
        }

        private class KioskLoginResponse
        {
            public string Token { get; set; } = "";
            public DateTime ExpiresAtUtc { get; set; }
            public string FullName { get; set; } = "";
            public string Role { get; set; } = "";
            public int UserId { get; set; }
            public string UserCode { get; set; } = "";
        }
    }
}
