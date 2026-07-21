using System;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using OmniKiosk.Wpf.Controls;
using OmniKiosk.Wpf.Sdk.Printer;
using OmniKiosk.Wpf.Services.MoneyExchange;
using OmniKiosk.Wpf.Services.MoneyReceiver;
using OmniKiosk.Wpf.Services;

namespace OmniKiosk.Wpf.Views.MoneyExchange.Steps
{
    public partial class CashInStep : UserControl, IStepNav
    {
        private readonly MoneyExchangeFlowController _ctl;
        private readonly MoneyReceiverService _svc = GlobalHardwareManager.MoneyReceiver;
        private readonly BixolonPrinterService _printerSvc = GlobalHardwareManager.Printer;

        public event EventHandler? NextRequested;
        public event EventHandler? BackRequested;
        public event EventHandler? ExitRequested;

        private double _totalForeign = 0;
        private double _pendingEscrowValue = 0;
        private int _maxMyrAvailable = 0;

        public CashInStep(MoneyExchangeFlowController ctl)
        {
            InitializeComponent();
            _ctl = ctl;
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            TitleText.Text = L10n.T("Mx_CashIn", "Insert Cash");
            SubtitleText.Text = L10n.T("Mx_CashInSubtitle", "Please insert your notes into the acceptor below.");
            RateLabel.Text = L10n.T("Mx_RateLabel", "Rate:");
            InsertedLabel.Text = L10n.T("Mx_TotalInserted", "TOTAL INSERTED (FOREIGN)");
            EquivalentLabel.Text = L10n.T("Mx_EquivalentAmount", "EQUIVALENT AMOUNT (MYR)");
            PayableLabel.Text = L10n.T("Mx_PayableAmount", "PAYABLE AMOUNT (ROUNDED)");
            TxtStatus.Text = L10n.T("Mx_MachineReady", "Machine is ready and accepting notes…");
            SlotHintText.Text = L10n.T("Mx_SlotHint", "Insert one note at a time. Wait for confirmation before inserting the next.");
            NoteDetectedLabel.Text = L10n.T("Mx_NoteDetected", "Note Detected");
            BtnEscrowReturn.Content = L10n.T("Mx_ReturnNote", "Return Note");
            BtnEscrowAccept.Content = L10n.T("Mx_AcceptNote", "Accept Note");
            DoneTitle.Text = L10n.T("Mx_AcceptanceStopped", "Acceptance Stopped");
            DoneSubtitle.Text = L10n.T("Mx_ProceedingToDispense", "Proceeding to dispense your cash…");
            BtnBack.Content = L10n.T("Mx_CancelTransaction", "Cancel Transaction");
            BtnNext.Content = L10n.T("Mx_FinishGetCash", "Finish & Get Cash ➔");

            TxtLiveRate.Text = $"1 {_ctl.State.FromCurrency} = {_ctl.State.RateToMyr:0.00} MYR";
            TxtInsertedForeign.Text = $"0.00 {_ctl.State.FromCurrency}";

            try
            {
                _maxMyrAvailable = GlobalHardwareManager.MoneyDispenser?.GetTotalAvailableMyr() ?? 5000;
            }
            catch { _maxMyrAvailable = 0; }

            if (_maxMyrAvailable <= 0)
            {
                CustomDialog.ShowError(
                    L10n.T("Mx_OutOfCashTitle", "Out of Cash"),
                    L10n.T("Mx_OutOfCashBody", "Sorry, this kiosk is currently out of MYR cash. Please proceed to the counter."));
                Task.Delay(1500).ContinueWith(_ => Dispatcher.Invoke(() => BackRequested?.Invoke(this, EventArgs.Empty)));
                return;
            }

            _svc.OnLog += Svc_OnLog;
            _svc.OnStatus += Svc_OnStatus;
            _svc.OnError += Svc_OnError;
            _svc.OnEscrow += Svc_OnEscrow;
            _svc.OnStacked += Svc_OnStacked;
            _svc.OnReturned += Svc_OnReturned;
            _svc.OnRejected += Svc_OnRejected;

            try { _svc.EnableAcceptance(true); } catch { }
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            try { _svc.EnableAcceptance(false); } catch { }

            _svc.OnLog -= Svc_OnLog;
            _svc.OnStatus -= Svc_OnStatus;
            _svc.OnError -= Svc_OnError;
            _svc.OnEscrow -= Svc_OnEscrow;
            _svc.OnStacked -= Svc_OnStacked;
            _svc.OnReturned -= Svc_OnReturned;
            _svc.OnRejected -= Svc_OnRejected;
        }

        // Hardware log events still fire for anyone wiring up file/telemetry
        // logging later - just no longer rendered as raw text in front of a
        // customer, per "neat, customer-facing" screens throughout this flow.
        private void Svc_OnLog(string s) => System.Diagnostics.Debug.WriteLine("[CashIn] " + s);
        private void Svc_OnStatus(string s) => Dispatcher.Invoke(() => TxtStatus.Text = s);
        private void Svc_OnError(string s) => System.Diagnostics.Debug.WriteLine("[CashIn:ERROR] " + s);

        private void Svc_OnRejected(string reason) => Dispatcher.Invoke(() =>
        {
            CustomDialog.ShowWarning(
                L10n.T("Mx_NoteRejectedTitle", "Note Rejected"),
                L10n.T("Mx_NoteRejectedBody", "The machine could not accept that note. Please flatten the note and try again, or try a different note.") + $" ({reason})");
        });

        private void Svc_OnEscrow(EscrowInfo info) => Dispatcher.Invoke(() =>
        {
            if (info == null || info.Value <= 0)
            {
                try { _svc.EscrowReturn(); } catch { }
                CustomDialog.ShowInfo(
                    L10n.T("Mx_InvalidDocTitle", "Invalid Document"),
                    L10n.T("Mx_InvalidDocBody", "This machine only accepts valid currency notes. Barcodes, coupons, or unrecognized items are not accepted."));
                return;
            }

            // SECURITY CHECK: Ensure it matches the requested currency
            string expectedCurrency = _ctl.State.FromCurrency;
            string insertedCurrency = string.IsNullOrWhiteSpace(info.CurrencyCode) ? "UNKNOWN" : info.CurrencyCode;

            bool isMatch = false;
            if (expectedCurrency.Length >= 2 && insertedCurrency.Length >= 2)
            {
                isMatch = insertedCurrency.Substring(0, 2).Equals(expectedCurrency.Substring(0, 2), StringComparison.OrdinalIgnoreCase);
            }

            if (!isMatch)
            {
                try { _svc.EscrowReturn(); } catch { }
                CustomDialog.ShowWarning(
                    L10n.T("Mx_InvalidCurrencyTitle", "Invalid Currency"),
                    string.Format(L10n.T("Mx_InvalidCurrencyBody", "Please insert {0} notes only. You inserted a {1} {2} note."), expectedCurrency, info.Value, insertedCurrency));
                return;
            }

            _pendingEscrowValue = info.Value;
            _totalForeign += _pendingEscrowValue;
            UpdateConversionUI();

            TxtEscrowAmount.Text = $"{info.Value} {expectedCurrency}";
            EscrowOverlay.Visibility = Visibility.Visible;
        });

        private void Svc_OnStacked(EscrowInfo info) => Dispatcher.Invoke(() =>
        {
            _pendingEscrowValue = 0;
            BtnNext.IsEnabled = true;

            if (_ctl.State.MyrAmount >= _maxMyrAvailable)
            {
                try { _svc.EnableAcceptance(false); } catch { }
                CustomDialog.ShowInfo(
                    L10n.T("Mx_LimitReachedTitle", "Limit Reached"),
                    string.Format(L10n.T("Mx_LimitReachedBody", "The machine only has RM {0} available. You cannot insert more notes. Please finish."), _maxMyrAvailable));
            }
        });

        private void Svc_OnReturned(EscrowInfo info) => Dispatcher.Invoke(() =>
        {
            if (_pendingEscrowValue > 0)
            {
                _totalForeign -= _pendingEscrowValue;
                if (_totalForeign < 0) _totalForeign = 0;

                _pendingEscrowValue = 0;
                UpdateConversionUI();
                BtnNext.IsEnabled = _totalForeign > 0;
            }
        });

        private void EscrowAccept_Click(object sender, RoutedEventArgs e)
        {
            EscrowOverlay.Visibility = Visibility.Collapsed;
            try { _svc.EscrowStack(); } catch { }
        }

        private void EscrowReturn_Click(object sender, RoutedEventArgs e)
        {
            EscrowOverlay.Visibility = Visibility.Collapsed;
            try { _svc.EscrowReturn(); } catch { }
        }

        private void UpdateConversionUI()
        {
            if (_totalForeign < 0) _totalForeign = 0;

            TxtInsertedForeign.Text = $"{_totalForeign:0.00} {_ctl.State.FromCurrency}";
            double exactMyr = _totalForeign * _ctl.State.RateToMyr;
            TxtEquivalentMyr.Text = $"RM {exactMyr:0.00}";

            double roundedMyr = Math.Floor(exactMyr);
            if (roundedMyr > _maxMyrAvailable) roundedMyr = _maxMyrAvailable;

            TxtPayableMyr.Text = $"RM {roundedMyr:0}";
            _ctl.State.FromAmount = _totalForeign;
            _ctl.State.MyrAmount = roundedMyr;
        }

        // Cancel is always available, but behaves differently depending on
        // whether any notes have actually gone into the vault - per the spec,
        // cancelling after notes are inserted needs confirmation and prints a
        // counter slip; cancelling with nothing inserted just leaves quietly.
        private async void Back_Click(object sender, RoutedEventArgs e)
        {
            if (_totalForeign <= 0)
            {
                BackRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            bool confirmed = CustomDialog.ShowQuestion(
                L10n.T("Mx_CancelConfirmTitle", "Cancel this transaction?"),
                string.Format(L10n.T("Mx_CancelConfirmBody", "You've already inserted {0:0.00} {1} ({2}). If you cancel now, we'll print a slip so you can collect this amount at the counter."), _totalForeign, _ctl.State.FromCurrency, TxtEquivalentMyr.Text),
                L10n.T("Mx_CancelConfirmYes", "Yes, Cancel"),
                L10n.T("Mx_CancelConfirmNo", "No, Continue"));

            if (!confirmed) return;

            try { _svc.EnableAcceptance(false); } catch { }
            PrintCancelSlip();
            await Task.Delay(300);
            ExitRequested?.Invoke(this, EventArgs.Empty);
        }

        // Same counter-slip pattern FinalReceiptStep already uses for a failed
        // dispense - reused here for a customer-initiated cancellation instead.
        private void PrintCancelSlip()
        {
            try
            {
                var s = _ctl.State;
                var custName = s.Customer?.FullName ?? "Walk-in Customer";
                var maskedDoc = ReceiptFormatter.MaskDocumentNo(s.Customer?.IdNo);
                // No transaction record exists yet at this point (cancellation
                // happens before CreateTransaction runs) - falls back to a
                // timestamp-based receipt number.
                var receiptNo = ReceiptFormatter.BuildReceiptNo(null);

                var r = new StringBuilder();
                r.Append(ReceiptFormatter.BuildHeader("CASH"));
                r.Append(BixolonPrinterService.ESC_BOLD_ON); r.Append("*** COUNTER COLLECTION SLIP ***\n"); r.Append(BixolonPrinterService.ESC_BOLD_OFF);
                r.Append(ReceiptFormatter.BuildCustomerBlock(receiptNo, custName, maskedDoc));

                r.Append(BixolonPrinterService.ESC_ALIGN_LEFT);
                r.Append("Status: TRANSACTION CANCELLED BY CUSTOMER\n");
                r.Append($"Inserted: {_totalForeign:0.00} {s.FromCurrency}\n");
                r.Append("--------------------------------\n");
                r.Append(BixolonPrinterService.ESC_ALIGN_CENTER); r.Append(BixolonPrinterService.ESC_BOLD_ON);
                r.Append("AMOUNT OWED TO CUSTOMER\n"); r.Append(BixolonPrinterService.ESC_DOUBLE_SIZE);
                r.Append($"RM {(_totalForeign * s.RateToMyr):0.00}\n"); r.Append(BixolonPrinterService.ESC_NORMAL_SIZE); r.Append(BixolonPrinterService.ESC_BOLD_OFF);

                r.Append(ReceiptFormatter.BuildFooter(success: false));

                _printerSvc?.PrintReceipt(r.ToString());
            }
            catch (Exception ex)
            {
                CustomDialog.ShowError(L10n.T("Mx_PrintErrorTitle", "Print Error"), ex.Message);
            }
        }

        private async void Next_Click(object sender, RoutedEventArgs e)
        {
            if (!BtnNext.IsEnabled) return;
            try { _svc.EnableAcceptance(false); } catch { }

            DoneOverlay.Visibility = Visibility.Visible;
            _ctl.State.CashInsertedMyr = _ctl.State.MyrAmount;
            _ctl.CreateTransaction();

            await Task.Delay(1500);
            NextRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
