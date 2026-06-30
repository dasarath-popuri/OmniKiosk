using System;
using System.Runtime.InteropServices;

namespace OmniKiosk.Wpf.Services.Remittance
{
    /// <summary>
    /// Service for printing receipts (thermal printer support)
    /// </summary>
    public class PrinterService
    {
        private readonly string _printerName;

        public PrinterService(string printerName = "POS80")
        {
            _printerName = printerName;
        }

        /// <summary>
        /// Print text to thermal printer with ESC/POS commands
        /// </summary>
        public void PrintText(string text)
        {
            try
            {
                // Add ESC/POS commands for thermal printer
                string escInit = "\x1B\x40";      // Initialize printer
                string escAlignCenter = "\x1B\x61\x01"; // Center align
                string escAlignLeft = "\x1B\x61\x00";   // Left align
                string escBold = "\x1B\x45\x01";  // Bold ON
                string escBoldOff = "\x1B\x45\x00"; // Bold OFF
                string escCut = "\x1D\x56\x00";   // Full cut
                string lineFeed = "\n";

                // Format receipt with ESC/POS commands
                string formattedReceipt = $"{escInit}{escAlignLeft}{text}{lineFeed}{lineFeed}{lineFeed}{escCut}";

                // Send to printer
                bool success = RawPrinterHelper.SendStringToPrinter(_printerName, formattedReceipt);

                if (success)
                {
                    //System.Windows.MessageBox.Show(
                    //    "Receipt printed successfully!",
                    //    "Print Success",
                    //    System.Windows.MessageBoxButton.OK,
                    //    System.Windows.MessageBoxImage.Information);
                }
                else
                {
                    //System.Windows.MessageBox.Show(
                    //    $"Failed to print to {_printerName}.\n\nPlease check:\n• Printer is powered on\n• Printer is connected\n• Printer name is correct",
                    //    "Printer Error",
                    //    System.Windows.MessageBoxButton.OK,
                    //    System.Windows.MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Printer error: {ex.Message}\n\nPlease contact technical support.",
                    "Printer Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Print text silently (no success message)
        /// </summary>
        public bool PrintTextSilent(string text)
        {
            try
            {
                // Add ESC/POS commands
                string escInit = "\x1B\x40";
                string escCut = "\x1D\x56\x00";
                string formattedReceipt = $"{escInit}{text}\n\n\n{escCut}";

                return RawPrinterHelper.SendStringToPrinter(_printerName, formattedReceipt);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Check if printer is available
        /// </summary>
        public bool IsPrinterAvailable()
        {
            try
            {
                // Try to open printer
                IntPtr hPrinter;
                bool result = RawPrinterHelper.OpenPrinter(_printerName, out hPrinter, IntPtr.Zero);
                if (result)
                {
                    RawPrinterHelper.ClosePrinter(hPrinter);
                }
                return result;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Get list of available printers (Windows Registry method)
        /// </summary>
        public static string[] GetAvailablePrinters()
        {
            try
            {
                // Read from Windows Registry
                var printers = new System.Collections.Generic.List<string>();
                string keyPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Print\Printers";

                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(keyPath))
                {
                    if (key != null)
                    {
                        foreach (string printerName in key.GetSubKeyNames())
                        {
                            printers.Add(printerName);
                        }
                    }
                }

                return printers.ToArray();
            }
            catch
            {
                return new string[] { "POS80" }; // Default fallback
            }
        }
    }

    /// <summary>
    /// Helper class for raw printer access (for thermal printers)
    /// </summary>
    public class RawPrinterHelper
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

        [DllImport("winspool.Drv", EntryPoint = "OpenPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool OpenPrinter([MarshalAs(UnmanagedType.LPStr)] string szPrinter, out IntPtr hPrinter, IntPtr pd);

        [DllImport("winspool.Drv", EntryPoint = "ClosePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "StartDocPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool StartDocPrinter(IntPtr hPrinter, Int32 level, [In, MarshalAs(UnmanagedType.LPStruct)] DOCINFOA di);

        [DllImport("winspool.Drv", EntryPoint = "EndDocPrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool EndDocPrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "StartPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool StartPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "EndPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool EndPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "WritePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, Int32 dwCount, out Int32 dwWritten);

        public static bool SendStringToPrinter(string szPrinterName, string szString)
        {
            IntPtr pBytes;
            Int32 dwCount;
            Int32 dwWritten = 0;
            IntPtr hPrinter = new IntPtr(0);
            DOCINFOA di = new DOCINFOA();
            bool bSuccess = false;

            di.pDocName = "Receipt Print";
            di.pDataType = "RAW";

            if (OpenPrinter(szPrinterName.Normalize(), out hPrinter, IntPtr.Zero))
            {
                if (StartDocPrinter(hPrinter, 1, di))
                {
                    if (StartPagePrinter(hPrinter))
                    {
                        dwCount = szString.Length;
                        pBytes = Marshal.StringToCoTaskMemAnsi(szString);
                        bSuccess = WritePrinter(hPrinter, pBytes, dwCount, out dwWritten);
                        Marshal.FreeCoTaskMem(pBytes);
                        EndPagePrinter(hPrinter);
                    }
                    EndDocPrinter(hPrinter);
                }
                ClosePrinter(hPrinter);
            }

            if (!bSuccess)
            {
                int error = Marshal.GetLastWin32Error();
                // Don't throw, just return false
                return false;
            }

            return bSuccess;
        }
    }
}