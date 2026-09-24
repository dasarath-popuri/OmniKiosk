using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Text.Json;
using OmniKiosk.Wpf.Config;

namespace OmniKiosk.Wpf.Services
{
    // Thrown specifically when the server reports this machine's MAC
    // address isn't in Ksk_Terminals at all - distinct from any other
    // failure (network down, wrong config, server error) so MainWindow's
    // startup code can catch this ONE case and show the dedicated
    // "this kiosk is not registered" full-screen block, rather than a
    // generic error dialog on top of a kiosk that shouldn't be usable yet.
    public sealed class KioskNotRegisteredException : Exception
    {
        public KioskNotRegisteredException(string message) : base(message) { }
    }

    // Thrown when Ksk_Terminals.Status for this machine is anything other
    // than 'Active' (AuthController.KioskLogin returns 423 Locked with
    // error code KIOSK_NOT_ACTIVE for this case) - distinct from
    // KioskNotRegisteredException, since "registered but taken offline for
    // maintenance" and "never registered at all" need different messaging
    // and, per instruction, a maintenance mode state rather than the
    // permanent-looking "not registered" block screen.
    public sealed class KioskMaintenanceException : Exception
    {
        public string ServerMessage { get; }
        public KioskMaintenanceException(string serverMessage) : base(serverMessage)
        {
            ServerMessage = serverMessage;
        }
    }

    // Machine-level login for this physical kiosk, not a human. Identity is
    // established purely by this machine's MAC address - no LoginId or
    // Password compiled into the app at all anymore. The server
    // (AuthController.KioskLogin) looks up Ksk_Terminals by that MAC,
    // resolves which UserProfile row it's allowed to authenticate as, and
    // returns KioskId/BranchId directly - no more parsing UserCode as a
    // stand-in for BranchId, no more a single hardcoded LoginId shared by
    // every physical kiosk running this same compiled build.
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
        private static string? _kioskId;
        private static int _branchId;
        private static DateTime _expiresAtUtc = DateTime.MinValue;
        private static readonly SemaphoreSlim _lock = new(1, 1);
        private static string? _cachedMacAddress;

        public static async Task<string> GetTokenAsync(CancellationToken ct = default)
        {
            await EnsureLoggedInAsync(ct);
            return _token!;
        }

        public static async Task<int> GetKioskUserIdAsync(CancellationToken ct = default)
        {
            await EnsureLoggedInAsync(ct);
            return _userId;
        }

        // Now comes directly from the kiosk-login response (Ksk_Terminals.
        // BranchId, resolved server-side) - not parsed from UserCode client-
        // side, which was the fragile, self-flagged-uncertain approach this
        // replaces.
        public static async Task<int> GetKioskBranchIdAsync(CancellationToken ct = default)
        {
            await EnsureLoggedInAsync(ct);
            return _branchId;
        }

        // The real per-kiosk identifier (e.g. "K-00001"), resolved server-
        // side from Ksk_Terminals by this machine's MAC address. This is
        // what every "K1" hardcoded placeholder in the rest of the app
        // should be replaced with.
        // The real KioskId is only meaningfully available via the async
        // login path above - but a few low-stakes, synchronous call sites
        // (receipt-number formatting fallbacks, not anything used for
        // DB matching or financial reconciliation) need a value without
        // being converted to async themselves. Safe to expose because by
        // the time any real transaction is happening, the app has already
        // logged in once at startup - this is just reading that already-
        // resolved cache, never triggering a new login itself. Returns
        // null if called before that first login has completed.
        public static string? CachedKioskIdOrNull => _kioskId;

        public static async Task<string> GetKioskIdAsync(CancellationToken ct = default)
        {
            await EnsureLoggedInAsync(ct);
            return _kioskId ?? throw new InvalidOperationException("KioskId was not returned by kiosk-login - this should not happen if login succeeded.");
        }

        private static async Task EnsureLoggedInAsync(CancellationToken ct)
        {
            if (_token != null && DateTime.UtcNow < _expiresAtUtc.AddSeconds(-60))
                return;

            await _lock.WaitAsync(ct);
            try
            {
                if (_token != null && DateTime.UtcNow < _expiresAtUtc.AddSeconds(-60))
                    return;

                var mac = GetThisKiosksMacAddress();

                var response = await _http.PostAsJsonAsync("api/v1/Auth/kiosk-login", new { MacAddress = mac }, ct);

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    // Confirmed specifically as the "not registered" case,
                    // not just any 404 - the server always returns this
                    // exact error code for that condition (AuthController.
                    // KioskLogin). 423/Locked (maintenance) is handled
                    // separately below; anything else still falls through
                    // to EnsureSuccessStatusCode as a generic failure.
                    string body = await response.Content.ReadAsStringAsync(ct);
                    bool isNotRegistered = false;
                    try
                    {
                        using var doc = JsonDocument.Parse(body);
                        isNotRegistered = doc.RootElement.TryGetProperty("error", out var err)
                            && err.GetString() == "KIOSK_NOT_REGISTERED";
                    }
                    catch { /* malformed body - fall through to generic failure below */ }

                    if (isNotRegistered)
                        throw new KioskNotRegisteredException(
                            $"This kiosk's MAC address ({mac}) is not registered in Ksk_Terminals. Contact support to register this terminal.");
                }

                if (response.StatusCode == HttpStatusCode.Locked)
                {
                    // KIOSK_NOT_ACTIVE - Ksk_Terminals.Status for this
                    // machine isn't 'Active'. Message comes from the
                    // server (already includes the real KioskId), not
                    // reconstructed client-side.
                    string body = await response.Content.ReadAsStringAsync(ct);
                    string serverMessage = "This kiosk is currently in maintenance mode.";
                    try
                    {
                        using var doc = JsonDocument.Parse(body);
                        if (doc.RootElement.TryGetProperty("message", out var msg) && msg.GetString() is string m)
                            serverMessage = m;
                    }
                    catch { /* use the default message above */ }

                    throw new KioskMaintenanceException(serverMessage);
                }

                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadFromJsonAsync<KioskLoginResponse>(cancellationToken: ct)
                    ?? throw new InvalidOperationException("Kiosk login returned an empty response.");

                _token = result.Token;
                _userId = result.UserId;
                _userCode = result.UserCode;
                _kioskId = result.KioskId;
                _branchId = result.BranchId ?? 0;
                _expiresAtUtc = result.ExpiresAtUtc;
            }
            finally
            {
                _lock.Release();
            }
        }

        public static void InvalidateToken()
        {
            _token = null;
            _userId = 0;
            _userCode = null;
            _kioskId = null;
            _branchId = 0;
            _expiresAtUtc = DateTime.MinValue;
        }

        // Selection rule, built from actually inspecting a real kiosk's
        // ipconfig /all output (a Fortinet VPN client, Wi-Fi Direct virtual
        // adapters, a USB WiFi dongle, and two onboard Intel Ethernet ports
        // that were both showing "Media disconnected" at the time):
        //
        //   1. Never a Loopback or Tunnel interface.
        //   2. Never anything whose description contains "Virtual", "VPN",
        //      "Fortinet", or "Wi-Fi Direct" - these are software-injected
        //      or vendor-shared, not unique per physical machine (Fortinet's
        //      virtual adapters specifically use a narrow shared vendor
        //      block, confirmed directly from real output).
        //   3. Deliberately does NOT filter by OperationalStatus - a real,
        //      correct onboard port can be legitimately unplugged at the
        //      moment this runs, and excluding it would fall through to a
        //      worse choice (the VPN, or a removable USB/wireless adapter).
        //   4. Prefers NetworkInterfaceType.Ethernet over wireless - an
        //      onboard port is physically part of the machine; a USB
        //      dongle is a five-second swap between machines and must not
        //      be trusted as identity.
        //   5. If more than one qualifying adapter remains, picks
        //      deterministically (sorted by MAC string, first one) so the
        //      SAME adapter is chosen every single boot of this specific
        //      machine, not whichever one Windows happens to enumerate
        //      first that day.
        //
        // *** This has been validated against one real machine's output,
        // not tested across your whole kiosk fleet. If a different kiosk's
        // hardware configuration doesn't match this shape (e.g. no onboard
        // Ethernet at all, WiFi-only), this logic will fall through to
        // whatever real Ethernet/Wireless adapter remains after the
        // exclusions - worth spot-checking ipconfig /all on a second,
        // differently-configured kiosk before trusting this everywhere. ***
        private static string GetThisKiosksMacAddress()
        {
            if (_cachedMacAddress != null) return _cachedMacAddress;

            var candidates = NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Where(nic => nic.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .Where(nic => !ContainsAny(nic.Description, "Virtual", "VPN", "Fortinet", "Wi-Fi Direct"))
                .Where(nic => !ContainsAny(nic.Name, "Virtual", "VPN", "Fortinet", "Wi-Fi Direct"))
                .ToList();

            var ethernetCandidates = candidates
                .Where(nic => nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
                .ToList();

            var pool = ethernetCandidates.Count > 0 ? ethernetCandidates : candidates;

            var chosen = pool
                .Select(nic => nic.GetPhysicalAddress().ToString())
                .Where(mac => !string.IsNullOrWhiteSpace(mac) && mac != "000000000000")
                .OrderBy(mac => mac, StringComparer.Ordinal)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(chosen))
                throw new InvalidOperationException(
                    "Could not find any real network adapter on this machine to use as its identity. " +
                    "Every adapter found was either virtual, a VPN, or had no physical address.");

            _cachedMacAddress = FormatMac(chosen);
            return _cachedMacAddress;
        }

        private static bool ContainsAny(string haystack, params string[] needles) =>
            needles.Any(n => haystack.Contains(n, StringComparison.OrdinalIgnoreCase));

        // GetPhysicalAddress().ToString() returns a bare hex string like
        // "CC827FAF83E0" - formatted here to match the dash-separated
        // style Ksk_Terminals.MacAddress should be registered in
        // ("CC-82-7F-AF-83-E0"), so a straight equality lookup works
        // server-side without either side needing to normalize.
        private static string FormatMac(string bareHex)
        {
            var parts = new List<string>();
            for (int i = 0; i < bareHex.Length; i += 2)
                parts.Add(bareHex.Substring(i, Math.Min(2, bareHex.Length - i)));
            return string.Join("-", parts);
        }

        private class KioskLoginResponse
        {
            public string Token { get; set; } = "";
            public DateTime ExpiresAtUtc { get; set; }
            public string FullName { get; set; } = "";
            public string Role { get; set; } = "";
            public int UserId { get; set; }
            public string UserCode { get; set; } = "";
            public string? KioskId { get; set; }
            public int? BranchId { get; set; }
        }
    }
}