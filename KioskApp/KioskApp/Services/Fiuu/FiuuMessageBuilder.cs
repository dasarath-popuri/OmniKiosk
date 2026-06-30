using System;
using System.Collections.Generic;

namespace OmniKiosk.Wpf.Services.Fiuu
{
    public static class FiuuMessageBuilder
    {
        public static object BuildLogin(FiuuConfig cfg)
        {
            var header = new Dictionary<string, object>
            {
                { "requestType", "Login" },
                { "service", "ECR" },
                { "datetime", DateTime.UtcNow.ToString("yyyyMMddHHmmss") }
            };

            var data = new Dictionary<string, object>
            {
                { "merchantId", cfg.MerchantId },
                { "posId", cfg.PosId },
                { "deviceId", cfg.DeviceId }
            };

            var signDict = header.Concat(data).ToDictionary(x => x.Key, x => x.Value);
            var signature = SignatureHelper.GenerateSignature(signDict, cfg.SecretKey);

            return new { header, data, signature };
        }

        public static object BuildSale(FiuuConfig cfg, string amount, string invoiceNo)
        {
            var header = new Dictionary<string, object>
            {
                { "requestType", "Sale" },
                { "service", "ECR" },
                { "datetime", DateTime.UtcNow.ToString("yyyyMMddHHmmss") }
            };

            var data = new Dictionary<string, object>
            {
                { "merchantId", cfg.MerchantId },
                { "posId", cfg.PosId },
                { "deviceId", cfg.DeviceId },
                { "amount", amount },      // "1000" => RM10.00
                { "invoiceNo", invoiceNo }
            };

            var signDict = header.Concat(data).ToDictionary(x => x.Key, x => x.Value);
            var signature = SignatureHelper.GenerateSignature(signDict, cfg.SecretKey);

            return new { header, data, signature };
        }

        public static object BuildHeartbeat(FiuuConfig cfg)
        {
            var header = new Dictionary<string, object>
            {
                { "requestType", "Heartbeat" },
                { "service", "ECR" },
                { "datetime", DateTime.UtcNow.ToString("yyyyMMddHHmmss") }
            };

            var data = new Dictionary<string, object>
            {
                { "merchantId", cfg.MerchantId },
                { "posId", cfg.PosId },
                { "deviceId", cfg.DeviceId }
            };

            var signDict = header.Concat(data).ToDictionary(x => x.Key, x => x.Value);
            var signature = SignatureHelper.GenerateSignature(signDict, cfg.SecretKey);

            return new { header, data, signature };
        }
    }
}
