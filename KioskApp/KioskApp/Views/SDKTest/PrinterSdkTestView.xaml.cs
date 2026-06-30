using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using OmniKiosk.Wpf.Sdk.Printer;

namespace OmniKiosk.Wpf.Views.SDKTest
{
    public partial class PrinterSdkTestView : UserControl
    {
        public event EventHandler? BackRequested;
        private readonly BixolonPrinterService _printerSvc = new BixolonPrinterService();

        public PrinterSdkTestView()
        {
            InitializeComponent();
            PreviewDate.Text = DateTime.Now.ToString("dd-MM-yyyy HH:mm");
        }

        private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

        private void PrintTest_Click(object sender, RoutedEventArgs e)
        {
            _printerSvc.PrinterName = TxtPrinterName.Text.Trim();

            StringBuilder sb = new StringBuilder();
            sb.Append(BixolonPrinterService.ESC_ALIGN_CENTER);
            sb.Append(BixolonPrinterService.ESC_BOLD_ON);
            sb.Append("OMNIREMIT EXCHANGE\n");
            sb.Append(BixolonPrinterService.ESC_BOLD_OFF);
            sb.Append("================================\n");
            sb.Append(BixolonPrinterService.ESC_ALIGN_LEFT);
            sb.Append($"Date: {DateTime.Now:dd-MM-yyyy HH:mm}\n");
            sb.Append("Type: BUY (USD)\n\n");
            sb.Append("Amount In:       100.00 USD\n");
            sb.Append("Rate:            4.7200\n");
            sb.Append(BixolonPrinterService.ESC_DOUBLE_SIZE);
            sb.Append("Amount Out:   RM 472.00\n");
            sb.Append(BixolonPrinterService.ESC_NORMAL_SIZE);
            sb.Append("\nThank you for your business!\n");
            sb.Append("================================\n");

            LogEvent("Sending test print job to: " + _printerSvc.PrinterName);
            bool success = _printerSvc.PrintReceipt(sb.ToString());

            LogEvent(success ? "✅ Print Job Sent Successfully (Cut & Eject applied)." : "❌ Print Failed. Check connection or printer name.");
        }

        private void CutAndEject_Click(object sender, RoutedEventArgs e)
        {
            _printerSvc.PrinterName = TxtPrinterName.Text.Trim();

            // Just send empty text with the PrintReceipt method, which natively cuts and ejects at the end.
            LogEvent("Sending manual Cut & Eject command...");
            bool success = _printerSvc.PrintReceipt("\n[Manual Hardware Cut Test]\n");

            LogEvent(success ? "✅ Hardware triggered." : "❌ Hardware trigger failed.");
        }

        private void LogEvent(string msg)
        {
            TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
            TxtLog.ScrollToEnd();
        }
    }
}