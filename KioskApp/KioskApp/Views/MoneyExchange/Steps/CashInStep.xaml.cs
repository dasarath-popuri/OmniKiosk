using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using OmniKiosk.Wpf.Services.MoneyExchange;
using OmniKiosk.Wpf.Services.MoneyReceiver;
using OmniKiosk.Wpf.Services;

namespace OmniKiosk.Wpf.Views.MoneyExchange.Steps
{
    public partial class CashInStep : UserControl, IStepNav
    {
        private readonly MoneyExchangeFlowController _ctl;
        private readonly MoneyReceiverService _svc = GlobalHardwareManager.MoneyReceiver;

        public event EventHandler NextRequested;
        public event EventHandler BackRequested;
        public event EventHandler ExitRequested;

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
            TxtLiveRate.Text = $"1 {_ctl.State.FromCurrency} = {_ctl.State.RateToMyr:0.00} MYR";
            TxtInsertedForeign.Text = $"0.00 {_ctl.State.FromCurrency}";

            try
            {
                _maxMyrAvailable = GlobalHardwareManager.MoneyDispenser?.GetTotalAvailableMyr() ?? 5000;
            }
            catch { _maxMyrAvailable = 0; }

            if (_maxMyrAvailable <= 0)
            {
                MessageBox.Show("Sorry, this Kiosk is currently out of MYR cash. Please proceed to the counter.", "Out of Cash", MessageBoxButton.OK, MessageBoxImage.Error);
                Task.Delay(1500).ContinueWith(_ => Dispatcher.Invoke(() => BackRequested?.Invoke(this, EventArgs.Empty)));
                return;
            }

            _svc.OnLog += Svc_OnLog;
            _svc.OnStatus += Svc_OnStatus;
            _svc.OnError += Svc_OnError;
            _svc.OnEscrow += Svc_OnEscrow;
            _svc.OnStacked += Svc_OnStacked;
            _svc.OnReturned += Svc_OnReturned;
            _svc.OnRejected += Svc_OnRejected; // Add this line

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
            _svc.OnRejected -= Svc_OnRejected; // Add this line
        }

        private void Svc_OnLog(string s) => Dispatcher.Invoke(() => AppendLog(s));
        private void Svc_OnStatus(string s) => Dispatcher.Invoke(() => TxtStatus.Text = $"• {s}");
        private void Svc_OnError(string s) => Dispatcher.Invoke(() => AppendLog("❌ " + s));
        private void Svc_OnRejected(string reason) => Dispatcher.Invoke(() =>
        {
            AppendLog($"⚠️ Note Rejected: {reason}");
            MessageBox.Show($"The machine could not accept that note.\n\nReason: {reason}\n\nPlease flatten the note and try again, or try a different note.",
                            "Note Rejected",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
        });

        private void Svc_OnEscrow(EscrowInfo info) => Dispatcher.Invoke(() =>
        {
            if (info == null || info.Value <= 0)
            {
                //AppendLog("⚠️ Unrecognized note. Auto-returning...");
                //try { _svc.EscrowReturn(); } catch { }
                //return;

                AppendLog("⚠️ Unrecognized or No-Value document. Auto-returning...");
                try { _svc.EscrowReturn(); } catch { }

                MessageBox.Show("This machine only accepts valid currency notes.\n\nBarcodes, coupons, or unrecognized items are not accepted.",
                                "Invalid Document",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
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
                AppendLog($"⚠️ MISMATCH: Expected {expectedCurrency} but got {insertedCurrency}. Spitting note out...");
                try { _svc.EscrowReturn(); } catch { }
                // Pops up an explicit warning that the user must acknowledge
                MessageBox.Show($"Please insert {expectedCurrency} notes only.\n\nYou inserted a {info.Value} {insertedCurrency} note.", "Invalid Currency", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            AppendLog($"✅ Valid {expectedCurrency} note detected in Escrow. Waiting for customer confirmation...");

            _pendingEscrowValue = info.Value;
            _totalForeign += _pendingEscrowValue;
            UpdateConversionUI();

            // EXPLICIT CONFIRMATION: Show the popup for customer confirmation
            if (TxtEscrowAmount != null)
            {
                TxtEscrowAmount.Text = $"{info.Value} {expectedCurrency}";
            }
            if (EscrowOverlay != null)
            {
                EscrowOverlay.Visibility = Visibility.Visible;
            }
        });

        private void Svc_OnStacked(EscrowInfo info) => Dispatcher.Invoke(() =>
        {
            AppendLog($"✅ Note securely dropped into vault.");

            _pendingEscrowValue = 0;
            BtnNext.IsEnabled = true;

            if (_ctl.State.MyrAmount >= _maxMyrAvailable)
            {
                AppendLog("⚠️ Machine limit reached. Disabling acceptor.");
                try { _svc.EnableAcceptance(false); } catch { }
                MessageBox.Show($"The machine only has RM {_maxMyrAvailable} available. You cannot insert more notes.\n\nPlease finish.", "Limit Reached", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        });

        private void Svc_OnReturned(EscrowInfo info) => Dispatcher.Invoke(() =>
        {
            AppendLog("⚠️ Note was spat back out to customer. Rolling back math...");

            if (_pendingEscrowValue > 0)
            {
                _totalForeign -= _pendingEscrowValue;
                if (_totalForeign < 0) _totalForeign = 0;

                _pendingEscrowValue = 0;
                UpdateConversionUI();
                BtnNext.IsEnabled = _totalForeign > 0;
            }
        });

        // 🚀 Physical UI Button Clicks for Confirmation
        private void EscrowAccept_Click(object sender, RoutedEventArgs e)
        {
            if (EscrowOverlay != null) EscrowOverlay.Visibility = Visibility.Collapsed;
            AppendLog("Customer clicked Accept. Stacking note...");
            try { _svc.EscrowStack(); } catch { }
        }

        private void EscrowReturn_Click(object sender, RoutedEventArgs e)
        {
            if (EscrowOverlay != null) EscrowOverlay.Visibility = Visibility.Collapsed;
            AppendLog("Customer clicked Return. Returning note...");
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

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (_totalForeign > 0) { MessageBox.Show("Notes have already been inserted. Please finish the transaction.", "Cannot Cancel", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            BackRequested?.Invoke(this, EventArgs.Empty);
        }

        private async void Next_Click(object sender, RoutedEventArgs e)
        {
            if (!BtnNext.IsEnabled) return;
            try { _svc.EnableAcceptance(false); } catch { }

            if (DoneOverlay != null) DoneOverlay.Visibility = Visibility.Visible;
            _ctl.State.CashInsertedMyr = _ctl.State.MyrAmount;
            _ctl.CreateTransaction();

            await Task.Delay(1500);
            NextRequested?.Invoke(this, EventArgs.Empty);
        }

        private void AppendLog(string msg) { TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n"); TxtLog.ScrollToEnd(); }
    }
}