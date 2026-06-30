using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Xml.Linq;
using OmniKiosk.Wpf.Controls;
using OmniKiosk.Wpf.Helpers;
using OmniKiosk.Wpf.Services;
using OmniKiosk.Wpf.Services.Remittance;
using OmniKiosk.Wpf.ViewModels.Remittance;
using NAudio.Wave;

namespace OmniKiosk.Wpf.Views.Remittance
{
    public partial class CDDPage : Page
    {
        private readonly RemittanceViewModel _vm;
        private readonly RateService _rateService;
        private readonly DeliveryMethodService _deliveryMethodService;
        private List<CountryInfo> _countries;
        private SessionTimeoutManager _sessionManager;
        private DispatcherTimer _marketingTimer;
        private MediaPlayer _welcomePlayer;
        private bool _hasPlayedWelcome = false;
        private IWavePlayer _wavePlayer;
        private AudioFileReader _audioFile;
        public CDDPage(RemittanceViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            _rateService = new RateService();
            _deliveryMethodService = new DeliveryMethodService();
            this.DataContext = _vm;

            LoadCountries();
            //this.Loaded += CDDPage_Loaded;

            // Force numeric keyboard for amount
            SendAmountTextBox.PreviewKeyDown += NumericTextBox_PreviewKeyDown;

            //InitializeMarketingBanner();

            this.Loaded += CDDPage_Loaded_WelcomeSound;
            this.Loaded += (s, e) => ApplyLocalization();
        }
        //private void CDDPage_Loaded(object sender, RoutedEventArgs e)
        //{
        //    // NOW the page is in the visual tree, so Window.GetWindow will work
        //    InitializeSessionTimeout();
        //}
        //private void InitializeSessionTimeout()
        //{
        //    var parentWindow = Window.GetWindow(this);
        //    if (parentWindow != null)
        //    {
        //        _sessionManager = new SessionTimeoutManager(parentWindow, OnSessionTimeout, 1);
        //        _sessionManager.Start();
        //    }
        //}

        //private void OnSessionTimeout()
        //{
        //    // Navigate back to CDDPage (restart transaction)
        //    if (this.NavigationService != null)
        //    {
        //        while (this.NavigationService.CanGoBack)
        //        {
        //            this.NavigationService.RemoveBackEntry();
        //        }
        //        this.NavigationService.Navigate(new CDDPage(_vm));
        //    }
        //}
        private void CDDPage_Loaded_WelcomeSound(object sender, RoutedEventArgs e)
        {
            if (!_hasPlayedWelcome)
            {
                // Delay slightly to ensure UI is fully loaded
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                timer.Tick += (s, ev) =>
                {
                    timer.Stop();
                    PlayWelcomeSound();
                };
                timer.Start();
                _hasPlayedWelcome = true;
            }
        }
        private void PlayWelcomeSound()
        {
            try
            {
                // Initialize MediaPlayer
                _welcomePlayer = new MediaPlayer();

                // CORRECTED PATH - Relative to your bin output directory
                string baseDir = System.AppDomain.CurrentDomain.BaseDirectory;
                string audioPath = System.IO.Path.Combine(baseDir, "Assets", "Audio", "welcome.mp3");


                if (System.IO.File.Exists(audioPath))
                {
                    var uri = new Uri(audioPath, UriKind.Absolute);
                    _welcomePlayer.Open(uri);
                    _welcomePlayer.Volume = 1.0; // Max volume for testing

                    _welcomePlayer.MediaOpened += (s, e) =>
                    {
                    };


                    _welcomePlayer.Play();
                }
                else
                {

                    // Try to find where the file actually is
                    string projectRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, @"..\..\.."));
                    string sourceAudioPath = System.IO.Path.Combine(projectRoot, "Assets", "Audio", "welcome.mp3");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"✗ EXCEPTION: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack: {ex.StackTrace}");
            }
        }
        // Don't forget to dispose
        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            _wavePlayer?.Dispose();
            _audioFile?.Dispose();
        }
    //    private void InitializeMarketingBanner()
    //    {
    //        var messages = new[]
    //        {
    //    "💸 Fast & Secure Money Transfers | Send Money Home Today!",
    //    "🌏 150+ Countries | Best Exchange Rates Guaranteed!",
    //    "⚡ Instant Transfers | Money Arrives in Minutes!",
    //    "🎁 Special Offer: Zero Fees on First Transfer!",
    //    "📱 Track Your Transfer | Real-time Updates via SMS!"
    //};

    //        MarketingContent.Children.Clear();

    //        foreach (var msg in messages)
    //        {
    //            var textBlock = new TextBlock
    //            {
    //                Text = msg,
    //                FontSize = 20,
    //                FontWeight = FontWeights.SemiBold,
    //                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB")),
    //                Margin = new Thickness(100, 0, 100, 0),
    //                VerticalAlignment = VerticalAlignment.Center
    //            };
    //            MarketingContent.Children.Add(textBlock);
    //        }

    //        // Start scrolling animation
    //        StartMarketingScroll();
    //    }

    //    private void StartMarketingScroll()
    //    {
    //        var translateTransform = new TranslateTransform();
    //        MarketingContent.RenderTransform = translateTransform;

    //        var animation = new DoubleAnimation
    //        {
    //            From = MarketingContent.ActualWidth,
    //            To = -MarketingContent.ActualWidth,
    //            Duration = TimeSpan.FromSeconds(30),
    //            RepeatBehavior = RepeatBehavior.Forever
    //        };

    //        translateTransform.BeginAnimation(TranslateTransform.XProperty, animation);
    //    }
        private void NumericTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Allow only numbers, decimal point, backspace, delete, tab, arrow keys
            if ((e.Key >= Key.D0 && e.Key <= Key.D9) ||
                (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9) ||
                e.Key == Key.Decimal || e.Key == Key.OemPeriod ||
                e.Key == Key.Back || e.Key == Key.Delete ||
                e.Key == Key.Tab || e.Key == Key.Left || e.Key == Key.Right)
            {
                e.Handled = false;
            }
            else
            {
                e.Handled = true;
            }
        }

        private void LoadCountries()
        {
            _countries = new List<CountryInfo>
            {
                new CountryInfo { CountryCode = "PH", CountryName = GetLocalizedString("Philippines"), Currency = "PHP", Flag = "🇵🇭" },
                new CountryInfo { CountryCode = "ID", CountryName = GetLocalizedString("Indonesia"), Currency = "IDR", Flag = "🇮🇩" },
                new CountryInfo { CountryCode = "IN", CountryName = GetLocalizedString("India"), Currency = "INR", Flag = "🇮🇳" },
                new CountryInfo { CountryCode = "SG", CountryName = GetLocalizedString("Singapore"), Currency = "SGD", Flag = "🇸🇬" },
                new CountryInfo { CountryCode = "TH", CountryName = GetLocalizedString("Thailand"), Currency = "THB", Flag = "🇹🇭" },
                new CountryInfo { CountryCode = "BD", CountryName = GetLocalizedString("Bangladesh"), Currency = "BDT", Flag = "🇧🇩" },
                new CountryInfo { CountryCode = "PK", CountryName = GetLocalizedString("Pakistan"), Currency = "PKR", Flag = "🇵🇰" },
                new CountryInfo { CountryCode = "NP", CountryName = GetLocalizedString("Nepal"), Currency = "NPR", Flag = "🇳🇵" },
                new CountryInfo { CountryCode = "LK", CountryName = GetLocalizedString("SriLanka"), Currency = "LKR", Flag = "🇱🇰" },
                new CountryInfo { CountryCode = "VN", CountryName = GetLocalizedString("Vietnam"), Currency = "VND", Flag = "🇻🇳" }
            };

            CountryComboBox.ItemsSource = _countries;
        }

        private string GetLocalizedString(string key)
        {
            // Implement localization based on _vm.SelectedLanguage
            // For now, return English defaults
            return key;
        }

        private void CountryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _sessionManager?.Reset();

            if (CountryComboBox.SelectedItem is CountryInfo selectedCountry)
            {
                SelectCountry(selectedCountry.CountryCode, selectedCountry.CountryName);
                LoadDeliveryMethods(selectedCountry.CountryCode);
                ShowNotification("Destination Selected", $"You selected {selectedCountry.CountryName}. Enter the amount to continue.", "success");
                CalculateTransaction();
            }
        }

        private void LoadDeliveryMethods(string countryCode)
        {
            CmbDeliveryMethod.Items.Clear();
            var deliveryMethods = _deliveryMethodService.GetDeliveryMethodsForCountry(countryCode);

            foreach (var method in deliveryMethods)
            {
                var comboBoxItem = new ComboBoxItem
                {
                    Content = $"{method.Icon} {method.Name}",
                    Tag = method.Code,
                    Foreground = new SolidColorBrush(Colors.Black)
                };
                CmbDeliveryMethod.Items.Add(comboBoxItem);
            }

            if (CmbDeliveryMethod.Items.Count > 0)
            {
                CmbDeliveryMethod.SelectedIndex = 0;
            }
        }

        private void SendAmountTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            _sessionManager?.Reset();

            Regex regex = new Regex(@"^[0-9]*\.?[0-9]*$");
            string currentText = SendAmountTextBox.Text;
            string newText = currentText.Insert(SendAmountTextBox.SelectionStart, e.Text);

            if (e.Text == "." && currentText.Contains("."))
            {
                e.Handled = true;
                return;
            }

            e.Handled = !regex.IsMatch(newText);
        }

        private void SendAmountTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _sessionManager?.Reset();
            CalculateTransaction();
        }

        private void SelectCountry(string countryCode, string countryName)
        {
            _vm.DestinationCountryCode = countryCode;
            _vm.DestinationCountry = countryName;
            _vm.BeneficiaryCountry = countryName; // FIX: Set country name here
            var countryInfo = GetCountryInfo(countryCode);
            _vm.ReceiveCurrency = countryInfo.Currency;
            _vm.ExchangeRate = _rateService.GetExchangeRate(countryCode);

            if (ExchangeRateDisplay != null)
            {
                string formattedRate = _rateService.FormatExchangeRate(_vm.ExchangeRate);
                ExchangeRateDisplay.Text = $"1 MYR = {formattedRate} {countryInfo.Currency}";
            }

            CalculateTransaction();
        }

        private void CalculateTransaction()
        {
            if (string.IsNullOrEmpty(_vm.DestinationCountryCode))
            {
                ClearCalculations();
                return;
            }

            if (string.IsNullOrWhiteSpace(SendAmountTextBox?.Text))
            {
                ClearCalculations();
                return;
            }

            if (decimal.TryParse(SendAmountTextBox.Text, out decimal sendAmount) && sendAmount > 0)
            {
                _vm.SendAmountMYR = sendAmount;
                var calculation = _rateService.CalculateTransaction(_vm.DestinationCountryCode, sendAmount);

                UpdateServiceFeeDisplay(calculation.ServiceFee);
                UpdateBeneficiaryAmountDisplay(calculation.ReceiveCurrency, calculation.ReceiveAmount);

                _vm.ServiceFee = calculation.ServiceFee;
                _vm.TotalAmountMYR = calculation.TotalCostMYR;
                _vm.ReceiveAmount = calculation.ReceiveAmount;

                ContinueButton.IsEnabled = true;
                AnimateTransactionSummary();
            }
            else
            {
                ClearCalculations();
            }
        }

        private void UpdateServiceFeeDisplay(decimal fee)
        {
            if (ServiceFeeDisplay != null)
            {
                ServiceFeeDisplay.Text = $"MYR {fee:N2}";
            }
        }

        private void UpdateBeneficiaryAmountDisplay(string currency, decimal amount)
        {
            if (BeneficiaryAmountDisplay != null)
            {
                BeneficiaryAmountDisplay.Text = $"{currency} {amount:N2}";
            }
        }

        private void ClearCalculations()
        {
            if (ServiceFeeDisplay != null)
                ServiceFeeDisplay.Text = "MYR 0.00";

            if (BeneficiaryAmountDisplay != null)
                BeneficiaryAmountDisplay.Text = "0.00";

            if (ContinueButton != null)
                ContinueButton.IsEnabled = false;
        }

        private void AnimateTransactionSummary()
        {
            if (TransactionSummaryPanel != null)
            {
                var scaleTransform = new ScaleTransform(1, 1);
                TransactionSummaryPanel.RenderTransform = scaleTransform;
                TransactionSummaryPanel.RenderTransformOrigin = new Point(0.5, 0.5);

                var scaleAnimation = new DoubleAnimation
                {
                    From = 0.98,
                    To = 1,
                    Duration = TimeSpan.FromMilliseconds(300),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };

                scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation);
                scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);
            }
        }

        private CountryInfo GetCountryInfo(string countryCode)
        {
            var country = _countries?.FirstOrDefault(c => c.CountryCode == countryCode);
            return country ?? new CountryInfo { CountryName = "Unknown", Currency = "XXX", CountryCode = countryCode, Flag = "🌍" };
        }

        private void Continue_Click(object sender, RoutedEventArgs e)
        {
            _sessionManager?.Reset();

            // Validation with visual feedback
            bool hasErrors = false;

            if (string.IsNullOrEmpty(_vm.DestinationCountryCode))
            {
                CustomDialog.ShowWarning(
                    "Destination Required",
                    "Please select a destination country first before continuing.",
                    "OK, I'll Select");
                return;
            }

            if (_vm.SendAmountMYR <= 0)
            {
                CustomDialog.ShowWarning(
                    "Amount Required",
                    "Please enter a valid amount to send before continuing.",
                    "OK, I'll Enter");
                SendAmountTextBox.Focus();
                return;
            }

            if (CmbDeliveryMethod.SelectedItem == null)
            {
                CustomDialog.ShowWarning(
                    "Delivery Method Required",
                    "Please select a delivery method before continuing.",
                    "OK, I'll Select");
                return;
            }

            var selectedItem = (ComboBoxItem)CmbDeliveryMethod.SelectedItem;
            _vm.DeliveryMethod = selectedItem.Content.ToString();
            _vm.DeliveryMethodCode = selectedItem.Tag.ToString();

            var validation = _vm.ValidateTransaction();
            if (!validation.IsValid)
            {
                CustomDialog.ShowWarning(
                    "Validation Error",
                    validation.ErrorMessage,
                    "OK, I Understand");
                return;
            }

            _sessionManager?.Stop();
            _vm.NavigateTo(RemittanceViewModel.NavigationTarget.Sender);
        }

        private void ShowNotification(string title, string message, string type = "success")
        {
            NotificationTitle.Text = title;
            NotificationMessage.Text = message;

            switch (type.ToLower())
            {
                case "success":
                    NotificationBanner.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                    NotificationIcon.Text = "✓";
                    break;
                case "info":
                    NotificationBanner.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB"));
                    NotificationIcon.Text = "ℹ";
                    break;
                case "warning":
                    NotificationBanner.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                    NotificationIcon.Text = "⚠";
                    break;
                case "error":
                    NotificationBanner.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                    NotificationIcon.Text = "✗";
                    break;
            }

            NotificationBanner.Visibility = Visibility.Visible;

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
            NotificationBanner.BeginAnimation(UIElement.OpacityProperty, fadeIn);

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            timer.Tick += (s, ev) =>
            {
                CloseNotification();
                timer.Stop();
            };
            timer.Start();
        }

        private void CloseNotification()
        {
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
            fadeOut.Completed += (s, e) => NotificationBanner.Visibility = Visibility.Collapsed;
            NotificationBanner.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }

        private void CloseNotification_Click(object sender, RoutedEventArgs e)
        {
            CloseNotification();
        }
        private void ApplyLocalization()
        {
            // Header
            TxtSendMoney.Text = $"💸 {LocalizationManager.GetString("SendMoney")}";
            TxtSubtitle.Text = LocalizationManager.GetString("FastSecureTransfers");

            // Step 1
            TxtSelectDestination.Text = LocalizationManager.GetString("SelectDestination");
            TxtChooseCountry.Text = LocalizationManager.GetString("ChooseCountry");

            // Step 2
            TxtSendAmount.Text = LocalizationManager.GetString("SendAmount");
            TxtHowMuch.Text = LocalizationManager.GetString("HowMuch");
            TxtDeliveryMethod.Text = LocalizationManager.GetString("DeliveryMethod");

            // Step 3
            TxtRemittanceDetails.Text = LocalizationManager.GetString("RemittanceDetails");
            TxtReviewDetails.Text = LocalizationManager.GetString("ReviewDetails");
            TxtExchangeRate.Text = LocalizationManager.GetString("ExchangeRate");
            TxtServiceFee.Text = LocalizationManager.GetString("ServiceFee");
            TxtBeneficiaryReceives.Text = LocalizationManager.GetString("BeneficiaryReceives");
            TxtLiveRate.Text = LocalizationManager.GetString("LiveRate");

            // Button
            //TxtNext.Text = LocalizationManager.GetString("Next");
            ContinueButton.Content = LocalizationManager.GetString("Next");


            // Trust Indicators
            TxtSecureTransfer.Text = LocalizationManager.GetString("SecureTransfer");
            TxtBankLevel.Text = LocalizationManager.GetString("BankLevel");
            TxtFastProcessing.Text = LocalizationManager.GetString("FastProcessing");
            TxtWithinMinutes.Text = LocalizationManager.GetString("WithinMinutes");
            TxtBestRates.Text = LocalizationManager.GetString("BestRates");
            TxtCompetitive.Text = LocalizationManager.GetString("Competitive");

            // Update "Select a country first" message
            if (string.IsNullOrEmpty(_vm.DestinationCountryCode))
            {
                ExchangeRateDisplay.Text = LocalizationManager.GetString("SelectCountryFirst");
            }
        }
        public class CountryInfo
        {
            public string CountryCode { get; set; }
            public string CountryName { get; set; }
            public string Currency { get; set; }
            public string Flag { get; set; }
        }
    }
}