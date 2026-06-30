using System;
using System.Linq;
using System.Text;

namespace OmniKiosk.Wpf.Services.Terminal
{
    public static class TerminalHelper
    {
        public static byte[] ParseHex(string hex)
        {
            hex = new string(hex.Where(c => !char.IsWhiteSpace(c)).ToArray());
            if (hex.Length % 2 != 0) throw new ArgumentException("Hex must be even length");
            return Enumerable.Range(0, hex.Length / 2)
                             .Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16))
                             .ToArray();
        }

        public static string BytesToHex(byte[] data) =>
            data == null ? "" : string.Join(" ", data.Select(b => b.ToString("X2")));

        public static byte[] To2Bcd(int value)
        {
            if (value < 0 || value > 9999) throw new ArgumentOutOfRangeException(nameof(value));
            int hi = value / 100, lo = value % 100;
            return new byte[]
            {
                (byte)(((hi / 10) << 4) | (hi % 10)),
                (byte)(((lo / 10) << 4) | (lo % 10))
            };
        }

        public static int BcdToInt(byte b1, byte b2) =>
            ((b1 >> 4) & 0x0F) * 1000 + (b1 & 0x0F) * 100 + ((b2 >> 4) & 0x0F) * 10 + (b2 & 0x0F);

        public static byte ComputeLrc(byte[] region)
        {
            byte lrc = 0;
            foreach (var b in region) lrc ^= b;
            return lrc;
        }

        public static string BytesToAscii(byte[] data)
        {
            var sb = new StringBuilder();
            foreach (var b in data)
                sb.Append(b >= 32 && b <= 126 ? (char)b : $"[{b:X2}]");
            return sb.ToString();
        }
    }
}
