using System;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using OmniKiosk.Wpf.Controls;
using OmniKiosk.Wpf.Helpers;
using OmniKiosk.Wpf.Services;
using OmniKiosk.Wpf.ViewModels.Remittance;

namespace OmniKiosk.Wpf.Views.Remittance
{
    public partial class RemittanceMainWindow : Window
    {
        private readonly RemittanceViewModel _vm;
        private SessionTimeoutManager _sessionManager; // Single session for entire remittance flow
        private bool _isDarkMode = false;
        private string _currentLanguage = "en";
        private bool _isSessionInitialized = false;

        public RemittanceMainWindow(string initialLanguage = "en")
        {
            InitializeComponent();

            _vm = new RemittanceViewModel();
            _vm.OnRequestNavigate += Vm_OnRequestNavigate;

            _currentLanguage = initialLanguage;
            ApplyLanguage(_currentLanguage);

            // Initialize session timeout after window is loaded
            this.Loaded += Window_Loaded;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Initialize the single session manager for the entire remittance flow
            if (!_isSessionInitialized)
            {
                InitializeSessionTimeout();
                _isSessionInitialized = true;
            }
            // Start with CDD (Country Selection) page
            MainFrame.Navigate(new CDDPage(_vm));
            UpdateStepIndicator("1 of 4", "Select Destination Country");
        }

        private void InitializeSessionTimeout()
        {
            // Create ONE session manager for the entire remittance flow
            // 2 minutes of inactivity before showing countdown
            _sessionManager = new SessionTimeoutManager(this, OnSessionTimeout, 1);
            _sessionManager.Start();

        }

        private void OnSessionTimeout()
        {

            Dispatcher.Invoke(() =>
            {
                // Clear all navigation history
                while (MainFrame.CanGoBack)
                {
                    MainFrame.RemoveBackEntry();
                }

                // Navigate back to first page (CDDPage)
                MainFrame.Navigate(new CDDPage(_vm));
                UpdateStepIndicator("1 of 4", "Select Destination Country");

                // Clear ViewModel data
                ClearTransactionData();

                // Restart the session timer
                _sessionManager?.Stop();
                _sessionManager?.Start();

                System.Diagnostics.Debug.WriteLine("RemittanceMainWindow: Session restarted successfully");
            });
        }

        private void ClearTransactionData()
        {
            // Clear all transaction data from ViewModel
            _vm.SendAmountMYR = 0;
            _vm.ServiceFee = 0;
            _vm.TotalAmountMYR = 0;
            _vm.ReceiveAmount = 0;
            _vm.DestinationCountryCode = null;
            _vm.BeneficiaryCountry = null;
            _vm.SelectedSender = null;
            _vm.SelectedBeneficiary = null;
            _vm.PurposeOfRemittance = null;
            _vm.DeliveryMethod = null;
            _vm.SourceOfFunds = null;
            _vm.TransactionReference = null;

            System.Diagnostics.Debug.WriteLine("RemittanceMainWindow: Transaction data cleared");
        }

        private void Vm_OnRequestNavigate(object sender, RemittanceViewModel.NavigationRequestedEventArgs e)
        {
            // Reset session timer on every navigation (user is active)
            _sessionManager?.Reset();

            switch (e.Target)
            {
                case RemittanceViewModel.NavigationTarget.CDD:
                    MainFrame.Navigate(new CDDPage(_vm));
                    UpdateStepIndicator("1 of 4", "Select Destination Country");
                    break;

                case RemittanceViewModel.NavigationTarget.Sender:
                    MainFrame.Navigate(new SenderPage(_vm));
                    UpdateStepIndicator("2 of 4", "Customer Information");
                    break;

                case RemittanceViewModel.NavigationTarget.Beneficiary:
                    MainFrame.Navigate(new BeneficiaryPage(_vm));
                    UpdateStepIndicator("3 of 4", "Beneficiary & Transaction");
                    break;

                case RemittanceViewModel.NavigationTarget.Summary:
                    MainFrame.Navigate(new SummaryPage(_vm));
                    UpdateStepIndicator("4 of 4", "Review & Payment");
                    break;

                case RemittanceViewModel.NavigationTarget.Back:
                    if (MainFrame.CanGoBack)
                    {
                        MainFrame.GoBack();
                        UpdateStepIndicatorFromCurrentPage();
                    }
                    break;
            }
        }

        private void UpdateStepIndicator(string step, string description)
        {
            TxtStepIndicator.Text = $"{step} - {description}";
        }

        private void UpdateStepIndicatorFromCurrentPage()
        {
            var currentPage = MainFrame.Content;

            if (currentPage is CDDPage)
                UpdateStepIndicator("1 of 4", "Select Destination Country");
            else if (currentPage is SenderPage)
                UpdateStepIndicator("2 of 4", "Customer Information");
            else if (currentPage is BeneficiaryPage)
                UpdateStepIndicator("3 of 4", "Beneficiary & Transaction");
            else if (currentPage is SummaryPage)
                UpdateStepIndicator("4 of 4", "Review & Payment");
        }

        private void ThemeToggle_Click(object sender, RoutedEventArgs e)
        {
            // Reset session timer on theme toggle (user activity)
            _sessionManager?.Reset();
            ToggleTheme();
        }

        private void ToggleTheme()
        {
            try
            {
                var appResources = Application.Current.Resources.MergedDictionaries;

                // Remove existing theme
                for (int i = appResources.Count - 1; i >= 0; i--)
                {
                    var rd = appResources[i];
                    if (rd.Source != null &&
                        (rd.Source.OriginalString.Contains("ThemeLight.xaml") ||
                         rd.Source.OriginalString.Contains("ThemeDark.xaml")))
                    {
                        appResources.RemoveAt(i);
                    }
                }

                // Add new theme
                var newTheme = new ResourceDictionary();
                if (!_isDarkMode)
                {
                    newTheme.Source = new Uri("/Themes/ThemeDark.xaml", UriKind.Relative);
                    _isDarkMode = true;
                }
                else
                {
                    newTheme.Source = new Uri("/Themes/ThemeLight.xaml", UriKind.Relative);
                    _isDarkMode = false;
                }
                appResources.Add(newTheme);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Theme switch error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyLanguage(string langCode)
        {
            try
            {
                TxtSendMoney.Text = LocalizationManager.GetString("SendMoney");
                var culture = new CultureInfo(langCode);
                Thread.CurrentThread.CurrentUICulture = culture;
                Thread.CurrentThread.CurrentCulture = culture;
            }
            catch { }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            // Reset session timer on close button click (user activity)
            _sessionManager?.Reset();

            //bool proceed = CustomDialog.ShowQuestion(
            //    "Confirm Exit",
            //    "Are you sure you want to exit?\nYour current transaction will be cancelled.",
            //    "Yes",
            //    "No");

            bool proceed = CustomDialog.ShowQuestion(
    LocalizationManager.GetString("ConfirmExit"),
    LocalizationManager.GetString("ExitConfirmMessage"),
    LocalizationManager.GetString("Yes"),
    LocalizationManager.GetString("No"));

            if (proceed)
            {
                // Stop session manager before closing
                _sessionManager?.Stop();
                this.Close();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            // Clean up session manager when window closes
            _sessionManager?.Stop();
            _sessionManager = null;

            System.Diagnostics.Debug.WriteLine("RemittanceMainWindow: Window closed, session manager cleaned up");

            base.OnClosed(e);
        }
    }
}