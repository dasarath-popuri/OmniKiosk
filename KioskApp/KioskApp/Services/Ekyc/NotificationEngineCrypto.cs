using System;
using System.Security.Cryptography;
using System.Text;

namespace OmniKiosk.Wpf.Services.Ekyc
{
    // Mirrors NotificationEngine.Security.CryptoEngine.Encrypt exactly:
    // TripleDES/ECB, key = MD5(UTF8(sharedSecret)).
    internal static class NotificationEngineCrypto
    {
        public static string Encrypt(string source, string sharedSecretKey)
        {
            using var md5 = MD5.Create();
            byte[] keyBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(sharedSecretKey));

            using var tdes = TripleDES.Create();
            tdes.Key = keyBytes;
            tdes.Mode = CipherMode.ECB;
            tdes.Padding = PaddingMode.PKCS7;

            byte[] plainBytes = Encoding.UTF8.GetBytes(source);
            using var encryptor = tdes.CreateEncryptor();
            byte[] cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
            return Convert.ToBase64String(cipherBytes);
        }
    }
}