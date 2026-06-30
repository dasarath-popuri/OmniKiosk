//using System.Net.Http;
//using System.Text;
//using System.Text.Json;

//namespace OmniKiosk.Wpf.Services.Fiuu
//{
//    public class FiuuEcrClient
//    {
//        private readonly HttpClient _http;
//        private readonly FiuuConfig _config;

//        public string? AccessToken { get; private set; }

//        public FiuuEcrClient(FiuuConfig config)
//        {
//            _config = config;
//            _http = new HttpClient { BaseAddress = new Uri(config.BaseUrl) };
//        }

//        public async Task<bool> LoginAsync()
//        {
//            var payload = new Dictionary<string, object>
//            {
//                { "merchantId", _config.MerchantId },
//                { "posId", _config.PosId },
//                { "apiVersion", "v1" },
//                { "datetime", DateTime.UtcNow.ToString("yyyyMMddHHmmss") }
//            };

//            //payload["signature"] = SignatureHelper.GenerateSignature(payload, _config.SecretKey);

//            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
//            var response = await _http.PostAsync("api/v1/login", content);
//            if (!response.IsSuccessStatusCode) return false;

//            var json = await response.Content.ReadAsStringAsync();
//            var result = JsonSerializer.Deserialize<LoginResponse>(json);

//            AccessToken = result?.accessToken;
//            _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AccessToken);
//            return true;
//        }

//        public async Task<SaleResponse?> SaleAsync(string amount, string invoiceNo)
//        {
//            if (string.IsNullOrEmpty(AccessToken))
//                throw new InvalidOperationException("You must login first.");

//            var payload = new Dictionary<string, object>
//    {
//        { "merchantId", _config.MerchantId },
//        { "posId", _config.PosId },
//        { "deviceId", _config.DeviceId },
//        { "amount", amount },               // "1000" => RM10.00
//        { "invoiceNo", invoiceNo },         // Unique invoice number
//        { "apiVersion", "v1" },
//        { "datetime", DateTime.UtcNow.ToString("yyyyMMddHHmmss") }
//    };

//            //payload["signature"] = SignatureHelper.GenerateSignature(payload, _config.SecretKey);

//            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
//            var response = await _http.PostAsync("api/v1/sale", content);

//            if (!response.IsSuccessStatusCode)
//                throw new Exception($"HTTP Error: {response.StatusCode}");

//            var json = await response.Content.ReadAsStringAsync();
//            return JsonSerializer.Deserialize<SaleResponse>(json);
//        }

//    }
//}
