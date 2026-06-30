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
using OmniKiosk.Wpf.Controls;
using OmniKiosk.Wpf.Helpers;
using OmniKiosk.Wpf.Models;
using OmniKiosk.Wpf.Services;
using OmniKiosk.Wpf.Services.Remittance;
using OmniKiosk.Wpf.ViewModels.Remittance;
using static System.Net.Mime.MediaTypeNames;

namespace OmniKiosk.Wpf.Views.Remittance
{
    public partial class BeneficiaryPage : Page
    {
        private readonly RemittanceViewModel _vm;
        private readonly DataStorageService _dataService;
        private readonly RateService _rateService;
        private List<BeneficiaryModel> _allBeneficiaries;
        private Dictionary<string, TextBox> _dynamicFields = new Dictionary<string, TextBox>();
        private SessionTimeoutManager _sessionManager;

        public BeneficiaryPage(RemittanceViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            _dataService = new DataStorageService();
            _rateService = new RateService();
            this.DataContext = _vm;
            LoadBanks();
            LoadBeneficiaries();
            LoadDynamicFields();
            //this.Loaded += BeneficiaryPage_Loaded;

            InitializeNumericFields(); 
            this.Loaded += (s, e) => ApplyLocalization();
        }

        //private void BeneficiaryPage_Loaded(object sender, RoutedEventArgs e)
        //{
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
        //    if (this.NavigationService != null)
        //    {
        //        while (this.NavigationService.CanGoBack)
        //        {
        //            this.NavigationService.RemoveBackEntry();
        //        }
        //        _vm.NavigateTo(RemittanceViewModel.NavigationTarget.CDD);
        //    }
        //}

        private void InitializeNumericFields()
        {
            // Mobile number - numeric only
            TxtBeneficiaryMobile.PreviewTextInput += NumericOnly_PreviewTextInput;
            TxtBeneficiaryMobile.PreviewKeyDown += NumericTextBox_PreviewKeyDown;

            // Account number - numeric only
            TxtAccountNumber.PreviewTextInput += NumericOnly_PreviewTextInput;
            TxtAccountNumber.PreviewKeyDown += NumericTextBox_PreviewKeyDown;
        }

        private void NumericOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            _sessionManager?.Reset();
            Regex regex = new Regex("[^0-9]+");
            e.Handled = regex.IsMatch(e.Text);
        }

        private void NumericTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if ((e.Key >= Key.D0 && e.Key <= Key.D9) ||
                (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9) ||
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

        private void LoadDynamicFields()
        {
            var requiredFields = BeneficiaryFieldRequirements.GetRequiredFields(
                _vm.DestinationCountryCode,
                _vm.DeliveryMethod);

            DynamicFieldsContainer.Children.Clear();
            _dynamicFields.Clear();

            if (requiredFields.Count == 0)
                return;

            foreach (var field in requiredFields)
            {
                var stackPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };

                var label = new TextBlock
                {
                    Text = field.Label,
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B")),
                    Margin = new Thickness(0, 0, 0, 8)
                };
                stackPanel.Children.Add(label);

                var border = new Border
                {
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0")),
                    BorderThickness = new Thickness(2),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(14, 0, 14, 0),
                    Background = Brushes.White
                };

                var textBox = new TextBox
                {
                    Height = 54,
                    FontSize = 17,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B")),
                    BorderThickness = new Thickness(0),
                    Background = Brushes.Transparent,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Tag = field.FieldName
                };

                var placeholderColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));
                var normalColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"));

                textBox.GotFocus += (s, e) =>
                {
                    _sessionManager?.Reset();
                    if (textBox.Text == field.Placeholder)
                    {
                        textBox.Text = "";
                        textBox.Foreground = normalColor;
                    }
                };

                textBox.LostFocus += (s, e) =>
                {
                    if (string.IsNullOrWhiteSpace(textBox.Text))
                    {
                        textBox.Text = field.Placeholder;
                        textBox.Foreground = placeholderColor;
                    }
                };

                textBox.Text = field.Placeholder;
                textBox.Foreground = placeholderColor;

                border.Child = textBox;
                stackPanel.Children.Add(border);
                DynamicFieldsContainer.Children.Add(stackPanel);
                _dynamicFields[field.FieldName] = textBox;
            }
        }

        private void LoadBanks()
        {
            var banks = _rateService.GetBanksForCountry(_vm.DestinationCountryCode);
            foreach (var bank in banks)
            {
                CmbBankName.Items.Add(new ComboBoxItem
                {
                    Content = bank.BankName,
                    Tag = bank.BankCode,
                    Foreground = new SolidColorBrush(Colors.Black)
                });
            }
        }

        private void LoadBeneficiaries()
        {
            if (!string.IsNullOrEmpty(_vm.CustomerIdNo))
            {
                _allBeneficiaries = _dataService.GetBeneficiariesByCustomerAndCountry(
                    _vm.CustomerIdNo,
                    _vm.DestinationCountryCode);
            }
            else
            {
                _allBeneficiaries = new List<BeneficiaryModel>();
            }

            BeneficiariesListBox.ItemsSource = _allBeneficiaries;

            if (_allBeneficiaries.Count == 0)
            {
                NoBeneficiariesMessage.Visibility = Visibility.Visible;
                BeneficiariesListBox.Visibility = Visibility.Collapsed;
            }
            else
            {
                NoBeneficiariesMessage.Visibility = Visibility.Collapsed;
                BeneficiariesListBox.Visibility = Visibility.Visible;
            }
        }

        private void ExistingBeneficiary_Click(object sender, RoutedEventArgs e)
        {
            _sessionManager?.Reset();

            ExistingBeneficiariesPanel.Visibility = Visibility.Visible;
            NewBeneficiaryPanel.Visibility = Visibility.Collapsed;
            BeneficiaryCreatedPanel.Visibility = Visibility.Collapsed;

            ExistingBeneficiaryButton.Style = (Style)FindResource("PrimaryButton");
            NewBeneficiaryButton.Style = (Style)FindResource("OutlineButton");
        }

        private void NewBeneficiary_Click(object sender, RoutedEventArgs e)
        {
            _sessionManager?.Reset();

            ExistingBeneficiariesPanel.Visibility = Visibility.Collapsed;
            NewBeneficiaryPanel.Visibility = Visibility.Visible;
            BeneficiaryCreatedPanel.Visibility = Visibility.Collapsed;

            ExistingBeneficiaryButton.Style = (Style)FindResource("OutlineButton");
            NewBeneficiaryButton.Style = (Style)FindResource("PrimaryButton");

            ClearForm();
            LoadDynamicFields();
        }

        private void SearchBeneficiary_TextChanged(object sender, TextChangedEventArgs e)
        {
            _sessionManager?.Reset();

            string searchText = SearchBeneficiaryTextBox.Text.ToLower();

            if (string.IsNullOrWhiteSpace(searchText))
            {
                BeneficiariesListBox.ItemsSource = _allBeneficiaries;
            }
            else
            {
                var filtered = _allBeneficiaries.Where(b =>
                    b.FullName.ToLower().Contains(searchText) ||
                    b.AccountNo.ToLower().Contains(searchText) ||
                    b.BankName.ToLower().Contains(searchText)).ToList();

                BeneficiariesListBox.ItemsSource = filtered;
            }
        }

        private void Beneficiary_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _sessionManager?.Reset();

            if (BeneficiariesListBox.SelectedItem is BeneficiaryModel beneficiary)
            {
                _vm.SelectedBeneficiary = beneficiary;
                _dataService.UpdateBeneficiaryLastUsed(beneficiary.Id);

                ShowNotification(
                    "Beneficiary Selected",
                    $"You have selected {beneficiary.FullName} at {beneficiary.BankName}. Please complete the transaction details below.",
                    "success");
            }
        }

        private void SaveBeneficiary_Click(object sender, RoutedEventArgs e)
        {
            _sessionManager?.Reset();

            // Clear previous validation
            ClearValidationStyles();

            bool hasErrors = false;

            // Validate required fields with visual feedback
            if (string.IsNullOrWhiteSpace(TxtBeneficiaryFirstName.Text))
            {
                SetValidationError(TxtBeneficiaryFirstName);
                hasErrors = true;
            }

            if (string.IsNullOrWhiteSpace(TxtBeneficiaryLastName.Text))
            {
                SetValidationError(TxtBeneficiaryLastName);
                hasErrors = true;
            }

            if (string.IsNullOrWhiteSpace(TxtBeneficiaryMobile.Text))
            {
                SetValidationError(TxtBeneficiaryMobile);
                hasErrors = true;
            }

            if (CmbRelationship.SelectedItem == null)
            {
                SetValidationError(CmbRelationship);
                hasErrors = true;
            }

            if (CmbBeneficiaryNationality.SelectedItem == null)
            {
                SetValidationError(CmbBeneficiaryNationality);
                hasErrors = true;
            }

            if (string.IsNullOrWhiteSpace(TxtAccountNumber.Text))
            {
                SetValidationError(TxtAccountNumber);
                hasErrors = true;
            }

            if (CmbBankName.SelectedItem == null)
            {
                SetValidationError(CmbBankName);
                hasErrors = true;
            }

            if (CmbBeneficiaryCountry.SelectedItem == null)
            {
                SetValidationError(CmbBeneficiaryCountry);
                hasErrors = true;
            }

            if (hasErrors)
            {
                //CustomDialog.ShowWarning(
                //    "Required Fields Missing",
                //    "Please fill in all required fields marked with * before saving.\n\n" +
                //    "OK, I'll Complete");
                CustomDialog.ShowWarning(
            LocalizationManager.GetString("Warning"),
            LocalizationManager.GetString("FillAllRequiredFields"),
            LocalizationManager.GetString("OK"));
                return;
            }

            // Validate dynamic fields
            var placeholderColor = (Color)ColorConverter.ConvertFromString("#94A3B8");
            foreach (var field in _dynamicFields)
            {
                var textBox = field.Value;
                var currentColor = ((SolidColorBrush)textBox.Foreground).Color;

                if (string.IsNullOrWhiteSpace(textBox.Text) || currentColor == placeholderColor)
                {
                    var border = VisualTreeHelper.GetParent(textBox) as Border;
                    if (border != null)
                    {
                        border.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                        border.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEF2F2"));
                    }
                    hasErrors = true;
                }
            }

            if (_vm.SelectedBeneficiary == null)
            {
                _vm.SelectedBeneficiary = new BeneficiaryModel();
            }

            _vm.BeneficiaryFullName = TxtBeneficiaryFirstName.Text.Trim() + " " + TxtBeneficiaryLastName.Text.Trim();
            _vm.BeneficiaryMobileNo = TxtBeneficiaryMobile.Text.Trim();
            _vm.BeneficiaryAddress = TxtBeneficiaryAddress.Text.Trim();
            _vm.BeneficiaryCity = TxtBeneficiaryCity.Text.Trim();
            _vm.BeneficiaryAccountNo = TxtAccountNumber.Text.Trim();

            var selectedBank = (ComboBoxItem)CmbBankName.SelectedItem;
            _vm.BeneficiaryBankName = selectedBank.Content.ToString();
            _vm.BeneficiaryBankCode = selectedBank.Tag?.ToString() ?? "";

            _vm.BeneficiaryRelationship = ExtractComboBoxText(CmbRelationship);
            _vm.BeneficiaryNationality = ExtractComboBoxText(CmbBeneficiaryNationality);
            _vm.BeneficiaryCountry = ExtractComboBoxText(CmbBeneficiaryCountry);

            // Save dynamic fields
            if (_dynamicFields.ContainsKey("IFSC"))
                _vm.SelectedBeneficiary.IFSC = _dynamicFields["IFSC"].Text;
            if (_dynamicFields.ContainsKey("RoutingNumber"))
                _vm.SelectedBeneficiary.RoutingNumber = _dynamicFields["RoutingNumber"].Text;
            if (_dynamicFields.ContainsKey("SwiftCode"))
                _vm.SelectedBeneficiary.SwiftCode = _dynamicFields["SwiftCode"].Text;
            if (_dynamicFields.ContainsKey("BankCode"))
                _vm.SelectedBeneficiary.BankCode = _dynamicFields["BankCode"].Text;
            if (_dynamicFields.ContainsKey("IBAN"))
                _vm.SelectedBeneficiary.IBAN = _dynamicFields["IBAN"].Text;

            try
            {
                _vm.SaveCurrentBeneficiary();
                LoadBeneficiaries();
                ShowBeneficiaryCreatedPanel();
            }
            catch (Exception ex)
            {
                CustomDialog.ShowError(
                    "Save Error",
                    $"Unable to save beneficiary details.\n\nError: {ex.Message}",
                    "Close");
            }
        }

        private void ShowBeneficiaryCreatedPanel()
        {
            NewBeneficiaryPanel.Visibility = Visibility.Collapsed;
            ExistingBeneficiariesPanel.Visibility = Visibility.Collapsed;

            CreatedBeneName.Text = _vm.BeneficiaryFullName;
            CreatedBeneBank.Text = _vm.BeneficiaryBankName;
            CreatedBeneCountry.Text = _vm.BeneficiaryCountry;
            CreatedBeneAccount.Text = _vm.BeneficiaryAccountNo;
            CreatedBeneMobile.Text = _vm.BeneficiaryMobileNo;

            BeneficiaryCreatedPanel.Visibility = Visibility.Visible;

            ShowNotification(
                "Beneficiary Created Successfully",
                $"{_vm.BeneficiaryFullName} has been added to your beneficiary list.",
                "success");
        }

        private void SetValidationError(Control control)
        {
            if (control is TextBox textBox)
            {
                var border = VisualTreeHelper.GetParent(textBox) as Border;
                if (border != null)
                {
                    border.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                    border.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEF2F2"));
                }
            }
            else if (control is ComboBox comboBox)
            {
                comboBox.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                comboBox.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEF2F2"));
            }
        }

        private void ClearValidationStyles()
        {
            var allControls = FindVisualChildren<Control>(this);
            foreach (var control in allControls)
            {
                if (control is TextBox textBox)
                {
                    var border = VisualTreeHelper.GetParent(textBox) as Border;
                    if (border != null)
                    {
                        border.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0"));
                        border.Background = Brushes.White;
                    }
                }
                else if (control is ComboBox comboBox)
                {
                    comboBox.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0"));
                    comboBox.Background = Brushes.White;
                }
            }
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj != null)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
                {
                    DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
                    if (child != null && child is T)
                    {
                        yield return (T)child;
                    }

                    foreach (T childOfChild in FindVisualChildren<T>(child))
                    {
                        yield return childOfChild;
                    }
                }
            }
        }

        private string ExtractComboBoxText(ComboBox comboBox)
        {
            if (comboBox.SelectedItem == null)
                return string.Empty;

            var selectedItem = comboBox.SelectedItem as ComboBoxItem;
            if (selectedItem == null)
                return string.Empty;

            if (selectedItem.Content is TextBlock textBlock)
            {
                return textBlock.Text ?? string.Empty;
            }

            return selectedItem.Content?.ToString() ?? string.Empty;
        }

        private void ClearForm()
        {
            TxtBeneficiaryFirstName.Clear();
            TxtBeneficiaryLastName.Clear();
            TxtBeneficiaryMobile.Clear();
            TxtBeneficiaryAddress.Clear();
            TxtBeneficiaryCity.Clear();
            TxtAccountNumber.Clear();
            CmbBankName.SelectedIndex = -1;
            CmbRelationship.SelectedIndex = -1;
            CmbBeneficiaryNationality.SelectedIndex = -1;
            CmbBeneficiaryCountry.SelectedIndex = -1;
            _dynamicFields.Clear();
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            _sessionManager?.Stop();
            _vm.NavigateTo(RemittanceViewModel.NavigationTarget.Back);
        }

        private void Continue_Click(object sender, RoutedEventArgs e)
        {
            _sessionManager?.Reset();

            if (_vm.SelectedBeneficiary == null && string.IsNullOrWhiteSpace(_vm.BeneficiaryFullName))
            {
                CustomDialog.ShowWarning(
                    "Beneficiary Required",
                    "Please select an existing beneficiary or create a new one before continuing.",
                    "OK, I'll Select");
                return;
            }

            if (CmbPurpose.SelectedItem == null || CmbSourceOfFunds.SelectedItem == null)
            {
                if (CmbPurpose.SelectedItem == null)
                    SetValidationError(CmbPurpose);
                if (CmbSourceOfFunds.SelectedItem == null)
                    SetValidationError(CmbSourceOfFunds);

                CustomDialog.ShowWarning(
                    "Transaction Details Required",
                    "Please select both the purpose of remittance and source of funds.",
                    "OK, I'll Complete");
                return;
            }

            var validation = _vm.ValidateTransaction();
            if (!validation.IsValid)
            {
                CustomDialog.ShowWarning(
                    "Validation Error",
                    validation.ErrorMessage,
                    "OK, I Understand");
                return;
            }

            _vm.PurposeOfRemittance = ExtractComboBoxText(CmbPurpose);
            _vm.SourceOfFunds = ExtractComboBoxText(CmbSourceOfFunds);

            _sessionManager?.Stop();
            _vm.NavigateTo(RemittanceViewModel.NavigationTarget.Summary);
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
            TxtBeneficiaryInfo.Text = $"💰 {LocalizationManager.GetString("BeneficiaryInformation")}";
            TxtSelectOrCreate.Text = LocalizationManager.GetString("SelectOrCreateBeneficiary");

            // Destination Card
            TxtSendingMoneyTo.Text = LocalizationManager.GetString("SendingMoneyTo");
            TxtExchangeRateLabel.Text = LocalizationManager.GetString("ExchangeRate");

            // Tab Buttons
            ExistingBeneficiaryButton.Content = $"📋 {LocalizationManager.GetString("SelectExistingBeneficiary")}";
            NewBeneficiaryButton.Content = $"➕ {LocalizationManager.GetString("CreateNewBeneficiary")}";

            // Existing Beneficiaries Panel
            TxtRecentBeneficiaries.Text = LocalizationManager.GetString("RecentBeneficiaries");
            TxtSelectFromPrevious.Text = LocalizationManager.GetString("SelectFromPrevious");
            //TxtSearchPlaceholder.Text = LocalizationManager.GetString("SearchByNameOrAccount");

            // No Beneficiaries Message
            TxtNoBeneficiaries.Text = LocalizationManager.GetString("NoBeneficiariesFound");
            TxtCreateToC.Text = LocalizationManager.GetString("CreateToContinue");

            // New Beneficiary Panel
            TxtEnterBeneficiaryDetails.Text = LocalizationManager.GetString("EnterBeneficiaryDetails");
            TxtCompleteFieldsBene.Text = LocalizationManager.GetString("CompleteRequiredFieldsBene");

            // Personal Information Group
            TxtPersonalInfo.Text = LocalizationManager.GetString("PersonalInformation");
            LblBeneFirstName.Text = LocalizationManager.GetString("FirstName") + " *";
            LblBeneLastName.Text = LocalizationManager.GetString("LastName") + " *";
            LblBeneMobile.Text = LocalizationManager.GetString("MobileNumber") + " *";
            LblRelationship.Text = LocalizationManager.GetString("Relationship") + " *";
            LblBeneNationality.Text = LocalizationManager.GetString("Nationality") + " *";

            // Address Information Group
            TxtAddressInfoBene.Text = LocalizationManager.GetString("AddressInformation");
            LblBeneAddress.Text = LocalizationManager.GetString("Address");
            LblBeneCity.Text = LocalizationManager.GetString("City");
            LblBeneCountry.Text = LocalizationManager.GetString("Country");

            // Bank Information Group
            TxtBankInfo.Text = LocalizationManager.GetString("BankInformation");
            LblBankName.Text = LocalizationManager.GetString("BankName") + " *";
            LblAccountNumber.Text = LocalizationManager.GetString("AccountNumber") + " *";

            // Success Panel
            TxtBeneCreatedSuccess.Text = LocalizationManager.GetString("BeneficiaryCreatedSuccess");
            TxtBeneProfileSaved.Text = LocalizationManager.GetString("BeneProfileSaved");

            // Transaction Details
            TxtRemittanceDetailsBene.Text = LocalizationManager.GetString("RemittanceDetails");
            TxtReviewTransferBene.Text = LocalizationManager.GetString("ReviewTransfer");
            TxtYouSend.Text = LocalizationManager.GetString("YouSend");
            TxtBeneReceives.Text = LocalizationManager.GetString("BeneficiaryReceives");
            TxtPurpose.Text = LocalizationManager.GetString("PurposeOfRemittance");
            TxtSourceOfFunds.Text = LocalizationManager.GetString("SourceOfFunds");
            TxtSendAmountLabel.Text = LocalizationManager.GetString("SendAmount");
            TxtServiceFeeLabel.Text = LocalizationManager.GetString("ServiceFee");
            TxtTotalAmount.Text = LocalizationManager.GetString("TotalAmount");

            // Navigation Buttons
            BackButton.Content = LocalizationManager.GetString("Back");
            ContinueButton.Content = LocalizationManager.GetString("Next");
        }
    }
}