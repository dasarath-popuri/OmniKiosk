using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.IO;
using System.Windows.Media.Imaging;
using OmniKiosk.Wpf.Controls;
using OmniKiosk.Wpf.Services.MoneyExchange;
using OmniKiosk.Wpf.Sdk.Printer;
using OmniKiosk.Wpf.Sdk.Dispenser;
using OmniKiosk.Wpf.Services;
using System.Threading.Tasks;
using System.Linq;

namespace OmniKiosk.Wpf.Views.MoneyExchange.Steps
{
    public partial class FinalReceiptStep : UserControl, IStepNav
    {
        private readonly MoneyExchangeFlowController _ctl;
        private readonly BixolonPrinterService _printerSvc = GlobalHardwareManager.Printer;
        private readonly PuloonDispenserService _dispenserSvc = GlobalHardwareManager.MoneyDispenser;
        private readonly MoneyExchangeApiClient _api = new();

        public event EventHandler? NextRequested;
        public event EventHandler? BackRequested;
        public event EventHandler? ExitRequested;

        private bool _dispenseSuccessful = false;
        private string _dispenseErrorMsg = "";

        public FinalReceiptStep(MoneyExchangeFlowController ctl)
        {
            InitializeComponent();
            _ctl = ctl;
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            TitleText.Text = L10n.T("Mx_Complete", "Transaction Complete");
            SubtitleText.Text = L10n.T("Mx_CollectCashSubtitle", "Please collect your cash from the dispenser below.");
            TotalDispensedLabel.Text = L10n.T("Mx_TotalDispensed", "TOTAL DISPENSED");
            VerifiedCustomerLabel.Text = L10n.T("Mx_VerifiedCustomer", "VERIFIED CUSTOMER");
            ForeignInsertedLabel.Text = L10n.T("Mx_ForeignInserted", "FOREIGN INSERTED");
            ExchangeRateLabel.Text = L10n.T("Mx_ExchangeRateLabel", "EXCHANGE RATE");
            BreakdownLabel.Text = L10n.T("Mx_NotesBreakdown", "Notes Dispensed Breakdown");
            BtnPrintReceipt.Content = "🖨️ " + L10n.T("Mx_PrintReceipt", "Print Receipt");
            BtnDone.Content = L10n.T("Mx_CompleteTransaction", "Complete Transaction");

            LoadTransactionData();

            // Cross-check against the DB-configured denominations before
            // dispensing - the dispenser hardware is hardcoded to exactly 4
            // fixed cassettes (DispenseAsync always takes 4 counts), so this
            // validates the DB agrees with that rather than trying to make
            // the physical dispense call itself dynamic. If the DB ever has
            // something other than exactly [100,50,10,1], that's a
            // configuration problem worth knowing about, not something to
            // silently paper over - falls back to the values already
            // calculated locally either way, so a failed API call never
            // blocks a customer from getting their cash.
            try
            {
                var apiDenoms = await _api.GetDenominationsAsync("MYR");
                var expected = new[] { 100, 50, 10, 1 };
                var actual = apiDenoms.Select(d => d.DenominationValue).ToArray();
                if (!expected.SequenceEqual(actual))
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[FinalReceipt] DB denominations [{string.Join(",", actual)}] don't match the 4 physical cassettes [{string.Join(",", expected)}] - dispensing with the hardcoded breakdown regardless, but this is worth fixing in Ksk_BanknoteDenominations.");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[FinalReceipt] Could not reach denominations API, continuing with local calculation: " + ex.Message);
            }

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
                        _dispenseErrorMsg = L10n.T("Mx_HardwareOffline", "Hardware Offline (Check USB Cable/Power)");
                        ShowDispenserNotice();
                        await CompleteTransactionSafeAsync("DispenseFailed");
                        return;
                    }
                }

                var response = await _dispenserSvc.DispenseAsync(c1, c2, c3, c4);

                if (response.Success)
                {
                    _dispenseSuccessful = true;
                    await CompleteTransactionSafeAsync("Completed");
                }
                else
                {
                    _dispenseSuccessful = false;
                    _dispenseErrorMsg = response.Message;
                    ShowDispenserNotice();
                    await CompleteTransactionSafeAsync("DispenseFailed");
                }
            }
            else
            {
                _dispenseSuccessful = true;
                await CompleteTransactionSafeAsync("Completed");
            }
        }

        // Cash has already physically moved (dispensed or not) by the time
        // this runs - a failed API call here must never be shown to the
        // customer or change what already happened at the machine.
        private async Task CompleteTransactionSafeAsync(string status)
        {
            if (!_ctl.State.TransactionId.HasValue) return;

            try
            {
                await _api.CompleteTransactionAsync(_ctl.State.TransactionId.Value, status);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FinalReceipt] Failed to mark transaction {_ctl.State.TransactionId} as {status}: {ex.Message}");
            }
        }

        private void ShowDispenserNotice()
        {
            CustomDialog.ShowWarning(
                L10n.T("Mx_DispenserNoticeTitle", "Dispenser Notice"),
                string.Format(L10n.T("Mx_DispenserNoticeBody", "The machine could not dispense the cash.\nReason: {0}\n\nPlease print your Counter Slip and proceed to the counter."), _dispenseErrorMsg));
        }

        private void LoadTransactionData()
        {
            var s = _ctl.State;
            TxtCustomer.Text = s.Customer?.FullName ?? "Walk-in";
            TxtForeign.Text = $"{s.FromAmount:0.00} {s.FromCurrency}";
            TxtRate.Text = $"{s.RateToMyr:0.0000}";
            TxtMyr.Text = $"RM {s.MyrAmount:0}";

            if (!string.IsNullOrWhiteSpace(s.LiveFaceImageBase64))
            {
                try
                {
                    var bytes = Convert.FromBase64String(s.LiveFaceImageBase64);
                    using var ms = new MemoryStream(bytes);
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    LiveFaceImage.Source = bmp;
                }
                catch { }
            }

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

            Row100.Visibility = count100 > 0 ? Visibility.Visible : Visibility.Collapsed;
            Row50.Visibility = count50 > 0 ? Visibility.Visible : Visibility.Collapsed;
            Row10.Visibility = count10 > 0 ? Visibility.Visible : Visibility.Collapsed;
            Row1.Visibility = count1 > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void PrintReceipt_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var s = _ctl.State;
                var custName = s.Customer?.FullName ?? "Walk-in Customer";
                var maskedDoc = ReceiptFormatter.MaskDocumentNo(s.Customer?.IdNo);
                var receiptNo = !string.IsNullOrWhiteSpace(s.ReceiptNo) ? s.ReceiptNo : ReceiptFormatter.BuildReceiptNo(s.TransactionId);

                var r = new StringBuilder();

                if (_dispenseSuccessful)
                {
                    r.Append(ReceiptFormatter.BuildHeader("CASH"));
                    r.Append(ReceiptFormatter.BuildCustomerBlock(receiptNo, custName, maskedDoc));

                    r.Append(BixolonPrinterService.ESC_ALIGN_CENTER);

                    r.Append(BixolonPrinterService.ESC_BOLD_ON);
                    r.Append("FOREIGN INSERTED\n");
                    r.Append(BixolonPrinterService.ESC_BOLD_OFF);
                    r.Append($"{s.FromAmount:0.00} {s.FromCurrency}\n\n");

                    r.Append(BixolonPrinterService.ESC_BOLD_ON);
                    r.Append("EXCHANGE RATE\n");
                    r.Append(BixolonPrinterService.ESC_BOLD_OFF);
                    r.Append($"{s.RateToMyr:0.0000}\n\n");

                    r.Append(BixolonPrinterService.ESC_BOLD_ON);
                    r.Append("TOTAL DISPENSED\n");
                    r.Append(BixolonPrinterService.ESC_DOUBLE_SIZE);
                    r.Append($"RM {s.MyrAmount:0.00}\n\n");
                    r.Append(BixolonPrinterService.ESC_NORMAL_SIZE);
                    r.Append(BixolonPrinterService.ESC_BOLD_OFF);

                    r.Append("--------------------------------\n");
                    r.Append(BixolonPrinterService.ESC_BOLD_ON);
                    r.Append("NOTES DISPENSED\n");
                    r.Append(BixolonPrinterService.ESC_BOLD_OFF);

                    int remaining = (int)s.MyrAmount;
                    int count100 = remaining / 100; remaining %= 100;
                    int count50 = remaining / 50; remaining %= 50;
                    int count10 = remaining / 10; remaining %= 10;
                    int count1 = remaining / 1;

                    if (count100 > 0) r.Append($"RM 100  x {count100}\n");
                    if (count50 > 0) r.Append($"RM 50   x {count50}\n");
                    if (count10 > 0) r.Append($"RM 10   x {count10}\n");
                    if (count1 > 0) r.Append($"RM 1    x {count1}\n");

                    r.Append(ReceiptFormatter.BuildFooter(success: true));
                }
                else
                {
                    r.Append(ReceiptFormatter.BuildHeader("CASH"));
                    r.Append(BixolonPrinterService.ESC_ALIGN_CENTER);
                    r.Append(BixolonPrinterService.ESC_BOLD_ON);
                    r.Append("*** COUNTER COLLECTION SLIP ***\n");
                    r.Append(BixolonPrinterService.ESC_BOLD_OFF);
                    r.Append(ReceiptFormatter.BuildCustomerBlock(receiptNo, custName, maskedDoc));

                    r.Append(BixolonPrinterService.ESC_ALIGN_CENTER);
                    r.Append("Status: MACHINE DISPENSE FAILED\n");
                    r.Append($"Reason: {_dispenseErrorMsg}\n");
                    r.Append("--------------------------------\n");
                    r.Append(BixolonPrinterService.ESC_BOLD_ON);
                    r.Append("AMOUNT OWED TO CUSTOMER\n");
                    r.Append(BixolonPrinterService.ESC_DOUBLE_SIZE);
                    r.Append($"RM {s.MyrAmount:0.00}\n");
                    r.Append(BixolonPrinterService.ESC_NORMAL_SIZE);
                    r.Append(BixolonPrinterService.ESC_BOLD_OFF);

                    r.Append(ReceiptFormatter.BuildFooter(success: false));
                }

                if (_printerSvc == null || !_printerSvc.PrintReceipt(r.ToString()))
                    CustomDialog.ShowError(L10n.T("Mx_PrintErrorTitle", "Print Error"), L10n.T("Mx_PrintErrorBody", "Failed to print. Check if the printer has paper and is connected."));
            }
            catch (Exception ex)
            {
                CustomDialog.ShowError(L10n.T("Mx_PrintErrorTitle", "Print Error"), ex.Message);
            }
        }
        private void Done_Click(object sender, RoutedEventArgs e) => ExitRequested?.Invoke(this, EventArgs.Empty);
    }
}
