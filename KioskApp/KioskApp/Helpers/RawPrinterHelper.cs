using System;
using System.IO;
using System.Runtime.InteropServices;

namespace OmniKiosk.Wpf.Helpers
{
    public static class RawPrinterHelper
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public class DOCINFOA
        {
            [MarshalAs(UnmanagedType.LPStr)]
            public string pDocName;
            [MarshalAs(UnmanagedType.LPStr)]
            public string pOutputFile;
            [MarshalAs(UnmanagedType.LPStr)]
            public string pDataType;
        }

        [DllImport("winspool.Drv", EntryPoint = "OpenPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true)]
        public static extern bool OpenPrinter(string szPrinter, out IntPtr hPrinter, IntPtr pd);

        [DllImport("winspool.Drv", SetLastError = true, ExactSpelling = true)]
        public static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "StartDocPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true)]
        public static extern bool StartDocPrinter(IntPtr hPrinter, int level, [In, MarshalAs(UnmanagedType.LPStruct)] DOCINFOA di);

        [DllImport("winspool.Drv", SetLastError = true, ExactSpelling = true)]
        public static extern bool EndDocPrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", SetLastError = true, ExactSpelling = true)]
        public static extern bool StartPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", SetLastError = true, ExactSpelling = true)]
        public static extern bool EndPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", SetLastError = true, ExactSpelling = true)]
        public static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);

        public static bool SendStringToPrinter(string printerName, string data)
        {
            IntPtr pBytes;
            Int32 dwCount = data.Length;
            Int32 dwWritten = 0;
            IntPtr hPrinter;
            DOCINFOA di = new DOCINFOA { pDocName = "Kiosk Print", pDataType = "RAW" };

            if (!OpenPrinter(printerName.Normalize(), out hPrinter, IntPtr.Zero)) return false;
            if (!StartDocPrinter(hPrinter, 1, di)) { ClosePrinter(hPrinter); return false; }
            if (!StartPagePrinter(hPrinter)) { EndDocPrinter(hPrinter); ClosePrinter(hPrinter); return false; }

            pBytes = Marshal.StringToCoTaskMemAnsi(data);
            bool success = WritePrinter(hPrinter, pBytes, dwCount, out dwWritten);
            Marshal.FreeCoTaskMem(pBytes);

            EndPagePrinter(hPrinter);
            EndDocPrinter(hPrinter);
            ClosePrinter(hPrinter);

            return success;
        }
    }
}
