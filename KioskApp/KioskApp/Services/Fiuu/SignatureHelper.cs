using System.Security.Cryptography;
using System.Text;

namespace OmniKiosk.Wpf.Services.Fiuu
{
    public static class SignatureHelper
    {
        public static string GenerateSignature(Dictionary<string, object> data, string secretKey)
        {
            var raw = string.Concat(
                data
                .Where(p => p.Value != null && p.Key != "signature")
                .OrderBy(p => p.Key)
                .Select(p => p.Value.ToString().Trim())
            );

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
            return BitConverter.ToString(hmac.ComputeHash(Encoding.UTF8.GetBytes(raw)))
                              .Replace("-", "").ToLower();
        }
    }
}
