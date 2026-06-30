using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OmniKiosk.Wpf.Services.Terminal
{
    public static class EcrMessageBuilder
    {
        private const byte STX = 0x02;
        private const byte ETX = 0x03;
        private const byte FS = 0x1C; // Field Separator

        // Real IDs you provided
        private const string TID = "00000001";        // 8 chars
        private const string MID = "000000000158870"; // 15 chars

        // ------------------ Core frame builder ------------------
        private static byte[] BuildFrame(byte[] messageData)
        {
            if (messageData == null) messageData = Array.Empty<byte>();

            // length = number of message bytes (everything between LEN and ETX exclusive? spec uses messageData length)
            var lenBcd = TerminalHelper.To2Bcd(messageData.Length);

            var frame = new List<byte>();
            frame.Add(STX);
            frame.AddRange(lenBcd);
            frame.AddRange(messageData);
            frame.Add(ETX);

            // LRC over LEN + messageData + ETX (i.e. bytes after STX up to ETX inclusive)
            var lrcRegion = new List<byte>();
            lrcRegion.AddRange(lenBcd);
            lrcRegion.AddRange(messageData);
            lrcRegion.Add(ETX);

            byte lrc = TerminalHelper.ComputeLrc(lrcRegion.ToArray());
            frame.Add(lrc);

            return frame.ToArray();
        }

        // helper - append ASCII bytes and FS
        private static void AppendField(List<byte> target, string fieldCode, string asciiData)
        {
            if (!string.IsNullOrEmpty(fieldCode))
                target.AddRange(Encoding.ASCII.GetBytes(fieldCode));
            if (!string.IsNullOrEmpty(asciiData))
                target.AddRange(Encoding.ASCII.GetBytes(asciiData));
            target.Add(FS);
        }

        // helper to build header with transport, TID, MID and presentation header
        // rrIndicator: '0' => request expecting response, '2' => no response required
        private static List<byte> BuildHeader(string txnCode, char rrIndicator)
        {
            var data = new List<byte>();

            // Transport header observed in working sample = ASCII '6'
            data.Add((byte)'6');

            // Terminal ID (8) then Merchant ID (15)
            data.AddRange(Encoding.ASCII.GetBytes(TID.PadRight(8, ' ')));
            data.AddRange(Encoding.ASCII.GetBytes(MID.PadRight(15, ' ')));

            // Presentation header: FormatVersion (1), R/R (1), TxnCode (2), RespCode (2), More (1), FS (1)
            data.Add((byte)'1');                    // Format version '1'
            data.Add((byte)rrIndicator);            // '0' or '2'
            data.AddRange(Encoding.ASCII.GetBytes(txnCode)); // e.g. "61"
            data.AddRange(Encoding.ASCII.GetBytes("00"));    // response code '00' for request
            data.Add((byte)'0');                    // more-to-follow '0'
            data.Add(FS);

            return data;
        }

        // ------------------ Transaction Builders ------------------

        // Ping (FF) - request expecting response
        public static byte[] BuildPing()
        {
            var data = BuildHeader("FF", '0');
            // no extra fields
            return BuildFrame(data.ToArray());
        }

        // Balance Inquiry '11'
        public static byte[] BuildBalanceInquiry(string payAccountId = "", bool receiptRequired = true)
        {
            var data = BuildHeader("11", '0');

            // Field 00: Pay Account ID (20 bytes)
            AppendField(data, "00", (payAccountId ?? "").PadRight(20, ' '));

            // Field 09: Receipt Required flag
            AppendField(data, "09", receiptRequired ? "1" : "0");

            return BuildFrame(data.ToArray());
        }

        // Sale '20' (amount: numeric string in smallest unit; invoiceNo up to 20 chars)
        public static byte[] BuildSale(string amount, string invoiceNo)
        {
            var data = BuildHeader("20", '0');

            // Field 00: Invoice/Reference (20)
            AppendField(data, "00", (invoiceNo ?? "").PadRight(20, ' '));

            // Field 01: Amount (12 digits zero-padded common format)
            string amt = (amount ?? "0").PadLeft(12, '0');
            AppendField(data, "01", amt);

            return BuildFrame(data.ToArray());
        }

        // Void '21' - uses original reference (invoice or trace)
        public static byte[] BuildVoid(string originalInvoiceNo)
        {
            var data = BuildHeader("21", '0');
            AppendField(data, "00", (originalInvoiceNo ?? "").PadRight(20, ' '));
            return BuildFrame(data.ToArray());
        }

        // Refund '22'
        public static byte[] BuildRefund(string amount, string invoiceNo)
        {
            var data = BuildHeader("22", '0');
            AppendField(data, "00", (invoiceNo ?? "").PadRight(20, ' '));
            AppendField(data, "01", (amount ?? "0").PadLeft(12, '0'));
            return BuildFrame(data.ToArray());
        }

        // Settlement '30'
        public static byte[] BuildSettlement()
        {
            var data = BuildHeader("30", '0');
            return BuildFrame(data.ToArray());
        }

        // Reprint Last '40'
        public static byte[] BuildReprintLast()
        {
            var data = BuildHeader("40", '2'); // fire-and-forget
            return BuildFrame(data.ToArray());
        }

        // Reprint Any '50' (use retrieval ref)
        public static byte[] BuildReprintAny(string retrievalRefNo)
        {
            var data = BuildHeader("50", '2');
            AppendField(data, "06", (retrievalRefNo ?? "").PadRight(12, ' '));
            return BuildFrame(data.ToArray());
        }

        // Print Receipt '60' (free text)
        public static byte[] BuildPrintReceipt(string receiptText)
        {
            var data = BuildHeader("60", '2'); // no response expected
            AppendField(data, "60", receiptText ?? "");
            return BuildFrame(data.ToArray());
        }

        // Display Screen '61' (text line up to 20 chars + timeout)
        public static byte[] BuildDisplayScreen(string message, int timeoutSeconds = 30)
        {
            var data = BuildHeader("61", '2'); // no response expected

            // Field 00: PayAccountID 20 bytes blank
            AppendField(data, "00", new string(' ', 20));

            // Field 61: Screen Text (N*20). We'll send one line padded to 20.
            AppendField(data, "61", (message ?? "").PadRight(20, ' '));

            // Field 62: Timeout 3 digits
            AppendField(data, "62", timeoutSeconds.ToString("D3"));

            return BuildFrame(data.ToArray());
        }
    }
}
