using OmniKiosk.Wpf.Services.MoneyReceiver;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Ports;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace OmniKiosk.Wpf.Views.SDKTest
{
    public partial class MoneyReceiverSdkTestView : UserControl
    {
        public event EventHandler? BackRequested;

        private readonly MoneyReceiverService _svc = new MoneyReceiverService();

        private Dictionary<string, double> _totals = new Dictionary<string, double>();

        private double _target = 0;
        private bool _accepting = false;
        private EscrowInfo? _escrow;

        public MoneyReceiverSdkTestView()
        {
            InitializeComponent();
        }

        private void Back_Click(object sender, RoutedEventArgs e)
            => BackRequested?.Invoke(this, EventArgs.Empty);

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            var ports = SerialPort.GetPortNames().OrderBy(p => p).ToList();
            if (ports.Count == 0) ports.Add("COM1");

            CmbPort.ItemsSource = ports;
            CmbPort.SelectedIndex = 0;

            _svc.OnLog += msg => Dispatcher.Invoke(() => AppendLog(msg));
            _svc.OnStatus += msg => Dispatcher.Invoke(() => TxtStatus.Text = msg);

            _svc.OnConnected += () => Dispatcher.Invoke(() => AppendLog("✅ Device connected"));
            _svc.OnRejected += reason => Dispatcher.Invoke(() => AppendLog($"❌ Rejected: {reason}"));
            _svc.OnDisconnected += () => Dispatcher.Invoke(() =>
            {
                AppendLog("⚠️ Device disconnected");
                _accepting = false;
            });

            _svc.OnError += err => Dispatcher.Invoke(() =>
            {
                AppendLog("❌ " + err);
                TxtStatus.Text = err;
            });

            _svc.OnEscrow += info => Dispatcher.Invoke(() =>
            {
                _escrow = info;

                string dirInfo = !string.IsNullOrWhiteSpace(info.Orientation) ? $" ({info.Orientation})" : "";
                AppendLog($"Event: Escrowed: Doc Type Bill = {info.CurrencyCode} {info.Value:0.00}{dirInfo}");

                TxtEscrowValue.Text = $"{info.CurrencyCode} {info.Value:0.00}{dirInfo}";
                TxtEscrowHint.Text = "Accept this note?";
                EscrowOverlay.Visibility = Visibility.Visible;
            });

            _svc.OnStacked += info => Dispatcher.Invoke(() =>
            {
                EscrowOverlay.Visibility = Visibility.Collapsed;
                _escrow = null;

                if (info == null || info.Value <= 0)
                {
                    LstNotes.Items.Insert(0, $"Accepted (value unknown)  ({DateTime.Now:HH:mm:ss})");
                    return;
                }

                string ccy = string.IsNullOrWhiteSpace(info.CurrencyCode) ? "UNKNOWN" : info.CurrencyCode;
                if (!_totals.ContainsKey(ccy)) _totals[ccy] = 0;

                _totals[ccy] += info.Value;

                LstNotes.Items.Insert(0, $"Accepted: {ccy} {info.Value:0.00}  ({DateTime.Now:HH:mm:ss})");
                UpdateUiTotals();
            });

            _svc.OnReturned += info => Dispatcher.Invoke(() =>
            {
                EscrowOverlay.Visibility = Visibility.Collapsed;
                _escrow = null;

                if (info != null && info.Value > 0)
                    LstNotes.Items.Insert(0, $"Returned: {info.CurrencyCode} {info.Value:0.00}  ({DateTime.Now:HH:mm:ss})");
                else
                    LstNotes.Items.Insert(0, $"Returned  ({DateTime.Now:HH:mm:ss})");
            });

            _svc.OnPupEscrow += info => Dispatcher.Invoke(() =>
            {
                _escrow = info;
                AppendLog($"⚠️ Recovered note from previous power loss: {info.CurrencyCode} {info.Value:0.00}");

                TxtEscrowValue.Text = $"{info.CurrencyCode} {info.Value:0.00}";
                TxtEscrowHint.Text = "Recovered Note. Accept?";
                EscrowOverlay.Visibility = Visibility.Visible;
            });

            TxtTotal.Text = "0.00";
            TxtTargetLabel.Text = "0.00";
            ProgTarget.Minimum = 0;
            ProgTarget.Value = 0;
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            try { _svc.Dispose(); } catch { }
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_svc.IsOpened)
                {
                    AppendLog("⚠️ Already attempted to open. Click Close first to reset.");
                    return;
                }

                var port = (CmbPort.SelectedItem?.ToString() ?? "").Trim();
                if (string.IsNullOrWhiteSpace(port))
                    throw new InvalidOperationException("Select a COM port.");

                AppendLog("⏳ Attempting to open " + port + "...");
                _svc.Open(port);
            }
            catch (Exception ex)
            {
                AppendLog("Open error: " + ex.Message);
                _svc.ForceResetState();
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                EscrowOverlay.Visibility = Visibility.Collapsed;
                _escrow = null;
                _accepting = false;

                _svc.Close();
                AppendLog("Closed");
            }
            catch (Exception ex) { AppendLog("Close error: " + ex.Message); }
        }

        private void StartAccept_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureOpened();
                _target = ParseTarget();
                TxtTargetLabel.Text = _target > 0 ? $"{_target:0.00}" : "No Limit";

                _accepting = true;
                _svc.EnableAcceptance(true);
                AppendLog("▶ Start accepting multi-currency");
            }
            catch (Exception ex) { AppendLog("StartAccept error: " + ex.Message); }
        }

        private void StopAccept_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StopAcceptInternal();
                AppendLog("⏹ Acceptance stopped");
            }
            catch (Exception ex) { AppendLog("StopAccept error: " + ex.Message); }
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            _totals.Clear();
            _target = 0;
            _accepting = false;

            TxtTarget.Text = "0";
            TxtTargetLabel.Text = "0.00";
            LstNotes.Items.Clear();

            UpdateUiTotals();
            AppendLog("Reset session");
        }

        private void EscrowStack_Click(object sender, RoutedEventArgs e) => TryStack();
        private void EscrowReturn_Click(object sender, RoutedEventArgs e) => TryReturn();

        private void TryStack()
        {
            try
            {
                if (_escrow == null)
                {
                    EscrowOverlay.Visibility = Visibility.Collapsed;
                    return;
                }

                _svc.EscrowStack();
                AppendLog($"Stack requested for {_escrow.CurrencyCode} {_escrow.Value:0.00}");
            }
            catch (Exception ex) { AppendLog("EscrowStack error: " + ex.Message); }
        }

        private void TryReturn()
        {
            try
            {
                if (_escrow == null)
                {
                    EscrowOverlay.Visibility = Visibility.Collapsed;
                    return;
                }

                _svc.EscrowReturn();
                AppendLog($"Return requested for {_escrow.CurrencyCode} {_escrow.Value:0.00}");
            }
            catch (Exception ex) { AppendLog("EscrowReturn error: " + ex.Message); }
        }

        private void StopAcceptInternal()
        {
            _accepting = false;
            try { _svc.EnableAcceptance(false); } catch { }
            UpdateUiTotals();
        }

        private void OpenDevice()
        {
            var port = (CmbPort.SelectedItem?.ToString() ?? "").Trim();
            if (string.IsNullOrWhiteSpace(port))
                throw new InvalidOperationException("Select a COM port.");

            _svc.Open(port);
            AppendLog("Opened on " + port);
        }

        private void EnsureOpened()
        {
            if (!_svc.IsOpened) OpenDevice();
        }

        private double ParseTarget()
        {
            var s = (TxtTarget.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(s)) return 0;
            if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) return Math.Max(0, v);
            return 0;
        }

        private void UpdateUiTotals()
        {
            if (_totals.Count == 0)
            {
                TxtTotal.Text = "0.00";
                TxtTotal.FontSize = 36;
            }
            else
            {
                var lines = _totals.Select(kvp => $"{kvp.Key} {kvp.Value:0.00}");
                TxtTotal.Text = string.Join("\n", lines);
                TxtTotal.FontSize = _totals.Count > 1 ? 24 : 36;
            }
        }

        private void AppendLog(string msg)
        {
            TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
            TxtLog.ScrollToEnd();
        }
    }
}