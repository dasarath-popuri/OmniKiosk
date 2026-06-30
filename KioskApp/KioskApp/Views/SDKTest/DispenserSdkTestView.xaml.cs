using System;
using System.Windows;
using System.Windows.Controls;
using OmniKiosk.Wpf.Sdk.Dispenser;
using OmniKiosk.Wpf.Services;

namespace OmniKiosk.Wpf.Views.SDKTest
{
    public partial class DispenserSdkTestView : UserControl
    {
        public event EventHandler BackRequested;
        private readonly PuloonDispenserService _dispenserSvc = GlobalHardwareManager.MoneyDispenser;

        public DispenserSdkTestView()
        {
            InitializeComponent();
        }

        private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

        private void Connect_Click(object sender, RoutedEventArgs e)
        {
            if (_dispenserSvc.Connect(TxtComPort.Text.Trim()))
            {
                LogEvent("✅ Connected to Puloon ECDM-400.");
                BtnDispense.IsEnabled = true;
                BtnTestDispense.IsEnabled = true;
            }
            else LogEvent("❌ Failed to open COM port.");
        }

        private void Disconnect_Click(object sender, RoutedEventArgs e)
        {
            _dispenserSvc.Disconnect();
            BtnDispense.IsEnabled = false;
            BtnTestDispense.IsEnabled = false;
            LogEvent("Disconnected.");
        }

        private async void Reset_Click(object sender, RoutedEventArgs e)
        {
            LogEvent("Sending Reset Command...");
            var res = await _dispenserSvc.ResetAsync();
            LogEvent(res.Success ? "✅ Reset Complete" : $"❌ Error: {res.Message}");
        }

        private async void Status_Click(object sender, RoutedEventArgs e)
        {
            LogEvent("Sending Status Command...");
            var res = await _dispenserSvc.StatusAsync();
            LogEvent(res.Success ? "✅ Status Normal" : $"❌ Status Error: {res.Message}");
        }

        private async void Purge_Click(object sender, RoutedEventArgs e)
        {
            LogEvent("Sending Purge Command...");
            var res = await _dispenserSvc.PurgeAsync();
            LogEvent(res.Success ? "✅ Purge Complete" : $"❌ Error: {res.Message}");
        }

        private async void Rom_Click(object sender, RoutedEventArgs e)
        {
            LogEvent("Sending ROM Version Command...");
            var res = await _dispenserSvc.RomVersionAsync();
            LogEvent(res.Success ? "✅ Read Complete" : $"❌ Error: {res.Message}");
        }

        private async void Dispense_Click(object sender, RoutedEventArgs e)
        {
            int c1 = int.Parse(TxtC1.Text); int c2 = int.Parse(TxtC2.Text);
            int c3 = int.Parse(TxtC3.Text); int c4 = int.Parse(TxtC4.Text);

            LogEvent($"Sending Live Dispense: [{c1}, {c2}, {c3}, {c4}]...");
            var res = await _dispenserSvc.DispenseAsync(c1, c2, c3, c4);
            LogEvent(res.Success ? $"✅ Dispensed successfully" : $"❌ Error: {res.Message}");
        }

        private async void TestDispense_Click(object sender, RoutedEventArgs e)
        {
            int c1 = int.Parse(TxtC1.Text); int c2 = int.Parse(TxtC2.Text);
            int c3 = int.Parse(TxtC3.Text); int c4 = int.Parse(TxtC4.Text);

            LogEvent($"Sending Test Dispense (to reject tray): [{c1}, {c2}, {c3}, {c4}]...");
            var res = await _dispenserSvc.TestDispenseAsync(c1, c2, c3, c4);
            LogEvent(res.Success ? $"✅ Test Dispense complete" : $"❌ Error: {res.Message}");
        }

        private void LogEvent(string msg) { TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n"); TxtLog.ScrollToEnd(); }
    }
}