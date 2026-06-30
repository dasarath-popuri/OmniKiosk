using System;
using System.Runtime.InteropServices;
using System.Text;

namespace OmniKiosk.Wpf.Sdk.Printer
{
    public class BixolonPrinterService
    {
        // Default Windows Driver name for Bixolon Thermal Printers
        public string PrinterName { get; set; } = "POS80";

        // Bixolon BK3-3 ESC/POS Commands
        public const string ESC_INIT = "\x1B\x40";
        public const string ESC_ALIGN_LEFT = "\x1B\x61\x00";
        public const string ESC_ALIGN_CENTER = "\x1B\x61\x01";
        public const string ESC_ALIGN_RIGHT = "\x1B\x61\x02";
        public const string ESC_BOLD_ON = "\x1B\x45\x01";
        public const string ESC_BOLD_OFF = "\x1B\x45\x00";
        public const string ESC_DOUBLE_SIZE = "\x1D\x21\x11";
        public const string ESC_NORMAL_SIZE = "\x1D\x21\x00";

        // BK3-3 Specific: Feed paper and Cut
        public const string ESC_CUT = "\x1D\x56\x42\x00";

        // BK3-3 Specific: Presenter Eject (Spits paper out of the kiosk bezel)
        public const string ESC_EJECT = "\x1D\x65\x03\x00";

        public bool PrintReceipt(string receiptContent)
        {
            try
            {
                StringBuilder rawData = new StringBuilder();

                // 1. Initialize Printer
                rawData.Append(ESC_INIT);

                // 2. Append Content
                rawData.Append(receiptContent);

                // 3. Feed a few lines
                rawData.Append("\n\n\n\n");

                // 4. Cut Paper
                rawData.Append(ESC_CUT);

                // 5. Eject from Kiosk Bezel (Presenter)
                rawData.Append(ESC_EJECT);

                return SendStringToPrinter(PrinterName, rawData.ToString());
            }
            catch
            {
                return false;
            }
        }

        // ========================================================
        // Raw Windows Spooler API (WinSpool.drv)
        // ========================================================
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private class DOCINFOA
        {
            [MarshalAs(UnmanagedType.LPStr)] public string pDocName;
            [MarshalAs(UnmanagedType.LPStr)] public string pOutputFile;
            [MarshalAs(UnmanagedType.LPStr)] public string pDataType;
        }

        [DllImport("winspool.Drv", EntryPoint = "OpenPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern bool OpenPrinter([MarshalAs(UnmanagedType.LPStr)] string szPrinter, out IntPtr hPrinter, IntPtr pd);

        [DllImport("winspool.Drv", EntryPoint = "ClosePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "StartDocPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern bool StartDocPrinter(IntPtr hPrinter, int level, [In, MarshalAs(UnmanagedType.LPStruct)] DOCINFOA di);

        [DllImport("winspool.Drv", EntryPoint = "EndDocPrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern bool EndDocPrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "StartPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern bool StartPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "EndPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern bool EndPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "WritePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);

        private static bool SendStringToPrinter(string printerName, string text)
        {
            IntPtr pBytes = Marshal.StringToCoTaskMemAnsi(text);
            int dwCount = text.Length;
            bool success = false;

            try
            {
                if (OpenPrinter(printerName.Normalize(), out IntPtr hPrinter, IntPtr.Zero))
                {
                    DOCINFOA di = new DOCINFOA { pDocName = "Kiosk Receipt", pDataType = "RAW" };
                    if (StartDocPrinter(hPrinter, 1, di))
                    {
                        if (StartPagePrinter(hPrinter))
                        {
                            success = WritePrinter(hPrinter, pBytes, dwCount, out _);
                            EndPagePrinter(hPrinter);
                        }
                        EndDocPrinter(hPrinter);
                    }
                    ClosePrinter(hPrinter);
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(pBytes);
            }
            return success;
        }
    }
}