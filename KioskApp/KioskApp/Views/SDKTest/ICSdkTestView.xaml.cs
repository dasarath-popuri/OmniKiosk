using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using OmniKiosk.Wpf.Sdk.IC;

namespace OmniKiosk.Wpf.Views.SDKTest
{
    public partial class ICSdkTestView : UserControl
    {
        public event EventHandler? BackRequested;
        private readonly IcReaderService _icSvc = new IcReaderService();

        public ICSdkTestView()
        {
            InitializeComponent();
        }

        private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

        private async void Read_Click(object sender, RoutedEventArgs e)
        {
            BtnRead.IsEnabled = false;
            LogEvent("Connecting and reading MyKad... Please do not remove card.");
            TxtName.Text = "-"; TxtIdNo.Text = "-"; TxtGender.Text = "-"; ImgPortrait.Source = null;

            var result = await _icSvc.ReadCardAsync();

            if (result.Data != null)
            {
                LogEvent($"Read Success: {result.Data.FullName} [{result.Data.IdNumber}]");

                TxtName.Text = result.Data.FullName;
                TxtIdNo.Text = result.Data.IdNumber;
                TxtGender.Text = result.Data.Gender;

                if (result.Data.PhotoBytes != null)
                {
                    try
                    {
                        using var ms = new MemoryStream(result.Data.PhotoBytes);
                        var bmp = new BitmapImage();
                        bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.StreamSource = ms; bmp.EndInit();
                        ImgPortrait.Source = bmp;
                        LogEvent("Photo loaded successfully.");
                    }
                    catch { LogEvent("Photo data was invalid or corrupted."); }
                }
            }
            else
            {
                LogEvent($"Error: {result.ErrorMessage}");
            }

            BtnRead.IsEnabled = true;
        }

        private void LogEvent(string msg)
        {
            TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
            TxtLog.ScrollToEnd();
        }
    }
}