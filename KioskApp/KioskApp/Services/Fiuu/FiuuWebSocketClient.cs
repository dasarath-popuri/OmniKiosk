using System;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using WebSocketSharp;

namespace OmniKiosk.Wpf.Services.Fiuu
{
    public class FiuuWebSocketClient
    {
        private readonly FiuuConfig _config;
        private WebSocket? _ws;

        public bool IsConnected => _ws?.IsAlive == true;

        public event Action<string>? OnMessage; // logs and responses
        public event Action<string>? OnError;

        public FiuuWebSocketClient(FiuuConfig config)
        {
            _config = config;
        }

        // ✅ Connect to WebSocket server
        public void Connect()
        {
            _ws = new WebSocket(_config.WebSocketUrl);

            _ws.OnOpen += (s, e) => OnMessage?.Invoke("✅ WebSocket Connected!");
            _ws.OnMessage += (s, e) => OnMessage?.Invoke("📩 Received: " + e.Data);
            _ws.OnError += (s, e) => OnError?.Invoke("❌ Error: " + e.Message);
            _ws.OnClose += (s, e) => OnMessage?.Invoke("🔌 WebSocket Closed");

            _ws.Connect();
        }

        public void Disconnect() => _ws?.Close();

        // ✅ Shared Signature Builder (HMAC-SHA256)
        private string GenerateSignature(Dictionary<string, object> payload)
        {
            var filtered = payload
                .Where(p => p.Key != "signature" && p.Value != null && p.Value.ToString()!.Trim() != "")
                .OrderBy(p => p.Key)
                .Select(p => p.Value.ToString()!.Trim());

            string raw = string.Concat(filtered);

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_config.SecretKey));
            return BitConverter.ToString(hmac.ComputeHash(Encoding.UTF8.GetBytes(raw)))
                             .Replace("-", "").ToLower();
        }

        private string Now() => DateTime.UtcNow.ToString("yyyyMMddHHmmss");

        // ✅ Generic Send
        private void SendPayload(Dictionary<string, object> payload)
        {
            payload["signature"] = GenerateSignature(payload);
            string json = JsonSerializer.Serialize(payload);

            _ws!.Send(json);
            OnMessage?.Invoke("➡ Sent: " + json);
        }

        // ✅ 1. LOGIN
        public void Login()
        {
            var payload = new Dictionary<string, object>
            {
                { "operation", "login" },
                { "merchantId", _config.MerchantId },
                { "posId", _config.PosId },
                { "apiVersion", "v1" },
                { "datetime", Now() }
            };
            SendPayload(payload);
        }

        // ✅ 2. HEARTBEAT
        public void Heartbeat()
        {
            var payload = new Dictionary<string, object>
            {
                { "operation", "heartbeat" },
                { "merchantId", _config.MerchantId },
                { "posId", _config.PosId },
                { "apiVersion", "v1" },
                { "datetime", Now() }
            };
            SendPayload(payload);
        }

        // ✅ 3. SALE
        public void Sale(string amount, string invoiceNo)
        {
            var payload = new Dictionary<string, object>
            {
                { "operation", "sale" },
                { "merchantId", _config.MerchantId },
                { "posId", _config.PosId },
                { "deviceId", _config.DeviceId },
                { "apiVersion", "v1" },
                { "amount", amount },        // "1000" = RM10.00
                { "invoiceNo", invoiceNo },
                { "datetime", Now() }
            };
            SendPayload(payload);
        }

        // ✅ 4. SETTLEMENT
        public void Settlement()
        {
            var payload = new Dictionary<string, object>
            {
                { "operation", "settlement" },
                { "merchantId", _config.MerchantId },
                { "posId", _config.PosId },
                { "apiVersion", "v1" },
                { "datetime", Now() }
            };
            SendPayload(payload);
        }
    }
}
