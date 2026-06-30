using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using OmniKiosk.Wpf.APIServices.Models.v1;

namespace OmniKiosk.Wpf.APIServices.Clients.v1
{
    public class OmniApiClient
    {
        private readonly HttpClient _httpClient;

        // Store the token so we don't have to keep logging in
        public static string CurrentJwtToken { get; private set; }

        public OmniApiClient()
        {
            _httpClient = new HttpClient();
            _httpClient.BaseAddress = new Uri("https://localhost:7001/api/v1/"); // Updated to v1!
            _httpClient.Timeout = TimeSpan.FromSeconds(30);

            // If we already have a token from a previous login, attach it automatically
            if (!string.IsNullOrEmpty(CurrentJwtToken))
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CurrentJwtToken);
            }
        }

        // 🚀 NEW: The Login Method
        public async Task<bool> AuthenticateKioskAsync(string branchCode, string password)
        {
            try
            {
                var loginData = new { BranchCode = branchCode, Password = password };
                var response = await _httpClient.PostAsJsonAsync("auth/login", loginData);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<AuthResponse>();
                    CurrentJwtToken = result.Token; // Save the token globally

                    // Attach it to the current client
                    _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CurrentJwtToken);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Login Error: {ex.Message}");
                return false;
            }
        }

        //public async Task<bool> SubmitTransactionAsync(TransactionDto tx)
        //{
        //    try
        //    {
        //        var response = await _httpClient.PostAsJsonAsync("transactions", tx);
        //        return response.IsSuccessStatusCode;
        //    }
        //    catch
        //    {
        //        return false;
        //    }
        //}
    }
}