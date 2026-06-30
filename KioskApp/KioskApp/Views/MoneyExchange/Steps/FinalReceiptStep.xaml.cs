using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.IO;
using System.Windows.Media.Imaging;
using OmniKiosk.Wpf.Services.MoneyExchange;
using OmniKiosk.Wpf.Sdk.Printer;
using OmniKiosk.Wpf.Sdk.Dispenser;
using OmniKiosk.Wpf.Services;
using System.Threading.Tasks;

namespace OmniKiosk.Wpf.Views.MoneyExchange.Steps
{
    public partial class FinalReceiptStep : UserControl, IStepNav
    {
        private readonly MoneyExchangeFlowController _ctl;
        private readonly BixolonPrinterService _printerSvc = GlobalHardwareManager.Printer;
        private readonly PuloonDispenserService _dispenserSvc = GlobalHardwareManager.MoneyDispenser;

        public event EventHandler NextRequested;
        public event EventHandler BackRequested;
        public event EventHandler ExitRequested;

        private bool _dispenseSuccessful = false;
        private string _dispenseErrorMsg = "";

        public FinalReceiptStep(MoneyExchangeFlowController ctl)
        {
            InitializeComponent();
            _ctl = ctl;
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            LoadTransactionData();

            // 🚀 FIX: Mapped exactly to your physical cassette order (Top to Bottom)
            int c1 = int.Parse(Txt1.Text);   // Cassette 1 (Top)    = RM 1
            int c2 = int.Parse(Txt10.Text);  // Cassette 2          = RM 10
            int c3 = int.Parse(Txt50.Text);  // Cassette 3          = RM 50
            int c4 = int.Parse(Txt100.Text); // Cassette 4 (Bottom) = RM 100

            if (c1 > 0 || c2 > 0 || c3 > 0 || c4 > 0)
            {
                if (!_dispenserSvc.IsConnected)
                {
                    string foundPort = await _dispenserSvc.AutoDetectDispenserPortAsync();

                    if (string.IsNullOrEmpty(foundPort))
                    {
                        _dispenseSuccessful = false;
                        _dispenseErrorMsg = "Hardware Offline (Check USB Cable/Power)";
                        MessageBox.Show($"Notice: The machine could not dispense the cash.\nReason: {_dispenseErrorMsg}\n\nPlease print your Counter Slip and proceed to the counter.", "Dispenser Notice", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                // Send the perfectly mapped notes to the dispenser
                var response = await _dispenserSvc.DispenseAsync(c1, c2, c3, c4);

                if (response.Success)
                {
                    _dispenseSuccessful = true;
                }
                else
                {
                    _dispenseSuccessful = false;
                    _dispenseErrorMsg = response.Message;

                    MessageBox.Show($"Notice: The machine could not dispense the cash.\nReason: {response.Message}\n\nPlease print your Counter Slip and proceed to the counter.", "Dispenser Notice", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            else
            {
                _dispenseSuccessful = true;
            }
        }

        //private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        //{
        //    LoadTransactionData();

        //    //int c1 = int.Parse(Txt100.Text);
        //    //int c2 = int.Parse(Txt50.Text);
        //    //int c3 = int.Parse(Txt10.Text);
        //    //int c4 = int.Parse(Txt1.Text);

        //    int c1 = int.Parse(Txt1.Text);   // Cassette 1 (Top)    = RM 1
        //    int c2 = int.Parse(Txt10.Text);  // Cassette 2          = RM 10
        //    int c3 = int.Parse(Txt50.Text);  // Cassette 3          = RM 50
        //    int c4 = int.Parse(Txt100.Text); // Cass

        //    if (c1 > 0 || c2 > 0 || c3 > 0 || c4 > 0)
        //    {
        //        // 🚀 NEW: Ensure we are actually connected to the dispenser before asking for cash!
        //        if (!_dispenserSvc.IsConnected)
        //        {
        //            string foundPort = await _dispenserSvc.AutoDetectDispenserPortAsync();

        //            if (string.IsNullOrEmpty(foundPort))
        //            {
        //                // The machine is totally offline or unplugged
        //                _dispenseSuccessful = false;
        //                _dispenseErrorMsg = "Hardware Offline (Check USB Cable/Power)";
        //                MessageBox.Show($"Notice: The machine could not dispense the cash.\nReason: {_dispenseErrorMsg}\n\nPlease print your Counter Slip and proceed to the counter.", "Dispenser Notice", MessageBoxButton.OK, MessageBoxImage.Warning);
        //                return;
        //            }
        //        }

        //        // Now that we are 100% sure we are connected, execute the dispense!
        //        var response = await _dispenserSvc.DispenseAsync(c1, c2, c3, c4);

        //        if (response.Success)
        //        {
        //            _dispenseSuccessful = true;
        //        }
        //        else
        //        {
        //            _dispenseSuccessful = false;
        //            _dispenseErrorMsg = response.Message;

        //            MessageBox.Show($"Notice: The machine could not dispense the cash.\nReason: {response.Message}\n\nPlease print your Counter Slip and proceed to the counter.", "Dispenser Notice", MessageBoxButton.OK, MessageBoxImage.Warning);
        //        }
        //    }
        //    else
        //    {
        //        _dispenseSuccessful = true;
        //    }
        //}

        private void LoadTransactionData()
        {
            var s = _ctl.State;
            TxtCustomer.Text = s.Customer?.FullName ?? "Walk-in";
            TxtForeign.Text = $"{s.FromAmount:0.00} {s.FromCurrency}";
            TxtRate.Text = $"{s.RateToMyr:0.0000}";
            TxtMyr.Text = $"RM {s.MyrAmount:0}";

            CalculateDispenserNotes((int)s.MyrAmount);
        }

        private void CalculateDispenserNotes(int totalMyr)
        {
            int remaining = totalMyr;
            int count100 = remaining / 100; remaining %= 100;
            int count50 = remaining / 50; remaining %= 50;
            int count10 = remaining / 10; remaining %= 10;
            int count1 = remaining / 1;

            Txt100.Text = count100.ToString();
            Txt50.Text = count50.ToString();
            Txt10.Text = count10.ToString();
            Txt1.Text = count1.ToString();
        }

        private void PrintReceipt_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var s = _ctl.State;
                var custName = s.Customer?.FullName ?? "Walk-in Customer";
                var txnId = s.TransactionId?.ToString() ?? Guid.NewGuid().ToString().Substring(0, 8).ToUpper();

                StringBuilder r = new StringBuilder();

                if (_dispenseSuccessful)
                {
                    r.Append(BixolonPrinterService.ESC_ALIGN_CENTER); r.Append(BixolonPrinterService.ESC_BOLD_ON); r.Append(BixolonPrinterService.ESC_DOUBLE_SIZE);
                    r.Append("OMNIREMIT\n"); r.Append(BixolonPrinterService.ESC_NORMAL_SIZE); r.Append("Official Money Exchange\n");
                    r.Append(BixolonPrinterService.ESC_BOLD_OFF); r.Append("================================\n");
                    r.Append(BixolonPrinterService.ESC_ALIGN_LEFT); r.Append($"Txn ID: {txnId}\n"); r.Append($"Date:   {DateTime.Now:dd-MM-yyyy HH:mm}\n"); r.Append($"Name:   {custName}\n");
                    r.Append("================================\n\n"); r.Append(BixolonPrinterService.ESC_BOLD_ON); r.Append($"EXCHANGE DETAILS\n"); r.Append(BixolonPrinterService.ESC_BOLD_OFF);
                    r.Append($"  Foreign In: {s.FromAmount:0.00} {s.FromCurrency}\n"); r.Append($"  Rate:       {s.RateToMyr:0.####}\n"); r.Append($"  Exact MYR:  RM {(s.FromAmount * s.RateToMyr):0.00}\n\n");
                    r.Append("--------------------------------\n"); r.Append(BixolonPrinterService.ESC_ALIGN_CENTER); r.Append(BixolonPrinterService.ESC_BOLD_ON);
                    r.Append("TOTAL DISPENSED\n"); r.Append(BixolonPrinterService.ESC_DOUBLE_SIZE); r.Append($"RM {s.MyrAmount:0}\n\n");
                    r.Append(BixolonPrinterService.ESC_NORMAL_SIZE); r.Append(BixolonPrinterService.ESC_BOLD_OFF); r.Append("================================\n");
                    r.Append("Thank you for using our kiosk!\n"); r.Append("Please collect your cash.\n");
                }
                else
                {
                    r.Append(BixolonPrinterService.ESC_ALIGN_CENTER); r.Append(BixolonPrinterService.ESC_BOLD_ON); r.Append(BixolonPrinterService.ESC_DOUBLE_SIZE);
                    r.Append("OMNIREMIT\n"); r.Append(BixolonPrinterService.ESC_NORMAL_SIZE); r.Append("Official Money Exchange\n");
                    r.Append(BixolonPrinterService.ESC_BOLD_OFF); r.Append("================================\n");
                    r.Append(BixolonPrinterService.ESC_BOLD_ON); r.Append("*** COUNTER COLLECTION SLIP ***\n"); r.Append(BixolonPrinterService.ESC_BOLD_OFF);
                    r.Append("================================\n");
                    r.Append(BixolonPrinterService.ESC_ALIGN_LEFT); r.Append($"Txn ID: {txnId}\n"); r.Append($"Date:   {DateTime.Now:dd-MM-yyyy HH:mm}\n"); r.Append($"Name:   {custName}\n\n");
                    r.Append($"Status: MACHINE DISPENSE FAILED\n");
                    r.Append($"Reason: {_dispenseErrorMsg}\n\n");
                    r.Append("--------------------------------\n"); r.Append(BixolonPrinterService.ESC_ALIGN_CENTER); r.Append(BixolonPrinterService.ESC_BOLD_ON);
                    r.Append("AMOUNT OWED TO CUSTOMER\n"); r.Append(BixolonPrinterService.ESC_DOUBLE_SIZE); r.Append($"RM {s.MyrAmount:0}\n\n");
                    r.Append(BixolonPrinterService.ESC_NORMAL_SIZE); r.Append(BixolonPrinterService.ESC_BOLD_OFF); r.Append("================================\n");
                    r.Append("Please present this slip at the\n"); r.Append("counter to collect your cash.\n");
                }

                if (_printerSvc == null || !_printerSvc.PrintReceipt(r.ToString()))
                    MessageBox.Show("Failed to print. Check if the printer has paper and is connected.");
            }
            catch (Exception ex) { MessageBox.Show("Print error: " + ex.Message); }
        }

        private void Done_Click(object sender, RoutedEventArgs e) => ExitRequested?.Invoke(this, EventArgs.Empty);
    }
}