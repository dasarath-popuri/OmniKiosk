using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using OmniKiosk.Wpf.Config;

namespace OmniKiosk.Wpf.Services
{
    public class MenuServiceItem
    {
        public string ServiceCode { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public int SortOrder { get; set; }
    }

    // Talks to OmniKiosk.Api (Config.Api) - the same API the kiosk already
    // authenticates against at boot via KioskAuthService, before any
    // service-specific flow (Money Exchange / Remittance) begins. Same
    // Bearer-token-with-retry-on-401 pattern as MoneyExchangeApiClient,
    // just pointed at a different BaseAddress.
    public sealed class MenuApiClient
    {
        private static readonly HttpClient _http = new HttpClient
        {
            BaseAddress = new Uri(KioskSettings.ConfigApiBaseUrl),
            Timeout = TimeSpan.FromSeconds(15)
        };

        private static async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> buildRequest, CancellationToken ct)
        {
            var token = await KioskAuthService.GetTokenAsync(ct);
            var request = buildRequest();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await _http.SendAsync(request, ct);
        }

        // Returns the enabled menu cards in SortOrder. On any failure,
        // returns an empty list rather than throwing - see MainWindow's
        // usage for the fallback behavior this triggers (both known cards
        // shown, so a Config.Api outage never means an empty, unusable
        // menu screen).
        public async Task<List<MenuServiceItem>> GetEnabledServicesAsync(CancellationToken ct = default)
        {
            try
            {
                var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, "api/v1/Menu/enabled-services"), ct);
                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadFromJsonAsync<List<MenuServiceItem>>(cancellationToken: ct);
                return result ?? new List<MenuServiceItem>();
            }
            catch
            {
                return new List<MenuServiceItem>();
            }
        }
    }
}