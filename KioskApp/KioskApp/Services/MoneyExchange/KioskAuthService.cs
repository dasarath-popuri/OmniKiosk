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
        private static DateTime _expiresAtUtc = DateTime.MinValue;
        private static readonly SemaphoreSlim _lock = new(1, 1);

        // Returns a valid token, logging in (or re-logging in) if the
        // cached one is missing or close to expiry. Safe to call from
        // multiple places concurrently - only one actual login request
        // happens even if several callers ask at the same moment.
        public static async Task<string> GetTokenAsync(CancellationToken ct = default)
        {
            // 60-second buffer so a token that's about to expire mid-request
            // doesn't get handed out and then rejected a moment later.
            if (_token != null && DateTime.UtcNow < _expiresAtUtc.AddSeconds(-60))
                return _token;

            await _lock.WaitAsync(ct);
            try
            {
                // Re-check after acquiring the lock - another caller may have
                // already refreshed it while this one was waiting.
                if (_token != null && DateTime.UtcNow < _expiresAtUtc.AddSeconds(-60))
                    return _token;

                var response = await _http.PostAsJsonAsync("api/v1/Auth/login", new
                {
                    LoginId = KioskSettings.KioskLoginId,
                    Password = KioskSettings.KioskLoginPassword
                }, ct);

                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadFromJsonAsync<KioskLoginResponse>(cancellationToken: ct)
                    ?? throw new InvalidOperationException("Kiosk login returned an empty response.");

                _token = result.Token;
                _expiresAtUtc = result.ExpiresAtUtc;
                return _token;
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
            _expiresAtUtc = DateTime.MinValue;
        }

        private class KioskLoginResponse
        {
            public string Token { get; set; } = "";
            public DateTime ExpiresAtUtc { get; set; }
            public string FullName { get; set; } = "";
            public string Role { get; set; } = "";
        }
    }
}
