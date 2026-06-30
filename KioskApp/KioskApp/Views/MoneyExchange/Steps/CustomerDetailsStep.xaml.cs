using OmniKiosk.Wpf.Config;
using OmniKiosk.Wpf.Models.MoneyExchange;
using OmniKiosk.Wpf.Sdk.IC;
using OmniKiosk.Wpf.Sdk.Passport;
using OmniKiosk.Wpf.Services.MoneyExchange;
using OmniKiosk.Wpf.Services; // GlobalManager
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace OmniKiosk.Wpf.Views.MoneyExchange.Steps
{
    public partial class CustomerDetailsStep : UserControl, IStepNav
    {
        private readonly MoneyExchangeFlowController _ctl;
        public event EventHandler? NextRequested;
        public event EventHandler? BackRequested;
        public event EventHandler? ExitRequested;

        // 🚀 Fetch from GlobalManager!
        private readonly PassportReaderService _svc = GlobalHardwareManager.PassportScanner;
        private readonly IcReaderService _icSvc = GlobalHardwareManager.IcReader;

        private CancellationTokenSource? _cts;
        private bool _isMalaysian = false;

        public CustomerDetailsStep(MoneyExchangeFlowController ctl)
        {
            InitializeComponent();
            _ctl = ctl;
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e) => ShowView("Selection");
        private void UserControl_Unloaded(object sender, RoutedEventArgs e) => StopPassportLoop();

        private void ShowView(string viewName)
        {
            ViewSelection.Visibility = Visibility.Collapsed;
            ViewScanning.Visibility = Visibility.Collapsed;
            ViewResult.Visibility = Visibility.Collapsed;
            BtnNext.Visibility = Visibility.Collapsed;

            if (viewName == "Selection") ViewSelection.Visibility = Visibility.Visible;
            else if (viewName == "Scanning") ViewScanning.Visibility = Visibility.Visible;
            else if (viewName == "Result") { ViewResult.Visibility = Visibility.Visible; BtnNext.Visibility = Visibility.Visible; }
        }

        private async void BtnMalaysian_Click(object sender, RoutedEventArgs e)
        {
            _isMalaysian = true; AutoReadStatus.Text = "Please insert your MyKad";
            ShowView("Scanning"); await StartIcScanAsync();
        }

        private void BtnForeigner_Click(object sender, RoutedEventArgs e)
        {
            _isMalaysian = false; AutoReadStatus.Text = "Please place your Passport";
            ShowView("Scanning"); StartPassportScan();
        }

        private async Task StartIcScanAsync()
        {
            StatusText.Text = "Reading MyKad securely...";
            if (_icSvc == null) { StatusText.Text = "IC Reader offline."; return; }

            var result = await _icSvc.ReadCardAsync();
            if (result.Data != null)
            {
                TxtName.Text = result.Data.FullName; TxtIdNo.Text = result.Data.IdNumber; TxtNat.Text = result.Data.Nationality;
                if (result.Data.PhotoBytes != null)
                {
                    try
                    {
                        using var ms = new MemoryStream(result.Data.PhotoBytes);
                        var bmp = new BitmapImage(); bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.StreamSource = ms; bmp.EndInit();
                        PortraitImage.Source = bmp;
                        if (_ctl.State.Customer == null) _ctl.State.Customer = new CustomerProfile();
                        _ctl.State.Customer.FaceImageBase64 = Convert.ToBase64String(result.Data.PhotoBytes);
                    }
                    catch { }
                }
                ShowView("Result");
            }
            else { StatusText.Text = "Hardware Read Failed. Please go back and try again."; }
        }

        private void StartPassportScan()
        {
            if (_svc == null)
            {
                StatusText.Text = "Passport Scanner missing or failed to boot.";
                return;
            }
            _cts = new CancellationTokenSource(); _ = Task.Run(() => PassportReadLoop(_cts.Token));
        }

        private void PassportReadLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (_svc.CheckOnlineEx() != 1) { Thread.Sleep(600); continue; }
                    if (_svc.TryReadPassport(out var doc, out var portraitPath))
                    {
                        if (string.IsNullOrWhiteSpace(doc.PassportNumber)) { Thread.Sleep(200); continue; }
                        Dispatcher.Invoke(() =>
                        {
                            TxtName.Text = doc.FullName ?? ""; TxtIdNo.Text = doc.PassportNumber ?? ""; TxtNat.Text = doc.Nationality ?? "";
                            if (!string.IsNullOrWhiteSpace(portraitPath) && File.Exists(portraitPath))
                            {
                                var bmp = new BitmapImage(); bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.UriSource = new Uri(portraitPath); bmp.EndInit();
                                PortraitImage.Source = bmp;
                                byte[] imageBytes = File.ReadAllBytes(portraitPath);
                                if (_ctl.State.Customer == null) _ctl.State.Customer = new CustomerProfile();
                                _ctl.State.Customer.FaceImageBase64 = Convert.ToBase64String(imageBytes);
                            }
                            ShowView("Result");
                        });
                        StopPassportLoop(); break;
                    }
                }
                catch { Thread.Sleep(500); }
            }
        }

        // IMPORTANT: Only cancel the token. DO NOT dispose the global _svc!
        private void StopPassportLoop() { try { _cts?.Cancel(); } catch { } }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (ViewScanning.Visibility == Visibility.Visible || ViewResult.Visibility == Visibility.Visible) { StopPassportLoop(); ShowView("Selection"); }
            else BackRequested?.Invoke(this, EventArgs.Empty);
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            if (_ctl.State.Customer == null) _ctl.State.Customer = new CustomerProfile();
            _ctl.State.Customer.IdType = _isMalaysian ? "IC" : "Passport";
            _ctl.State.Customer.IdNo = TxtIdNo.Text; _ctl.State.Customer.FullName = TxtName.Text; _ctl.State.Customer.Nationality = TxtNat.Text;
            _ctl.UpsertCustomer(_ctl.State.Customer); NextRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}