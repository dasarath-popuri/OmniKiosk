using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OmniKiosk.Wpf.Controls;
using OmniKiosk.Wpf.Helpers;
using OmniKiosk.Wpf.Models;
using OmniKiosk.Wpf.Services;
using OmniKiosk.Wpf.ViewModels.Remittance;

namespace OmniKiosk.Wpf.Views.Remittance
{
    public partial class SenderPage : Page
    {
        private readonly RemittanceViewModel _vm;
        private readonly DataStorageService _dataService;
        private byte[] _capturedPhotoData;
        private string _verifiedMobileNo;
        private const string HARDCODED_OTP = "000000";
        private SessionTimeoutManager _sessionManager;

        public SenderPage(RemittanceViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            _dataService = new DataStorageService();
            this.DataContext = _vm;

            //this.Loaded += SenderPage_Loaded;

            //InitializeNumericFields();
            this.Loaded += (s, e) => ApplyLocalization();
        }

        //private void SenderPage_Loaded(object sender, RoutedEventArgs e)
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
            // Mobile number fields - numeric only
            TxtVerifyMobileNo.PreviewTextInput += NumericOnly_PreviewTextInput;
            TxtVerifyMobileNo.PreviewKeyDown += NumericTextBox_PreviewKeyDown;
            TxtDisplayMobileNo.PreviewTextInput += NumericOnly_PreviewTextInput;

            // OTP field - numeric only
            TxtOTP.PreviewTextInput += NumericOnly_PreviewTextInput;
            TxtOTP.PreviewKeyDown += NumericTextBox_PreviewKeyDown;

            // Postcode field - numeric only
            TxtPostcode.PreviewTextInput += NumericOnly_PreviewTextInput;
            TxtPostcode.PreviewKeyDown += NumericTextBox_PreviewKeyDown;
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
        private void VerifyIdentity_Click(object sender, RoutedEventArgs e)
        {
            _sessionManager?.Reset();

            // Clear previous validation
            ClearValidationStyles();

            bool hasErrors = false;

            if (CmbVerifyICType.SelectedItem == null)
            {
                SetValidationError(CmbVerifyICType);
                hasErrors = true;
            }

            if (string.IsNullOrWhiteSpace(TxtVerifyICNumber.Text))
            {
                SetValidationError(TxtVerifyICNumber);
                hasErrors = true;
            }

            if (CmbVerifyNationality.SelectedItem == null)
            {
                SetValidationError(CmbVerifyNationality);
                hasErrors = true;
            }

            if (string.IsNullOrWhiteSpace(TxtVerifyMobileNo.Text))
            {
                SetValidationError(TxtVerifyMobileNo);
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

            string mobileNo = TxtVerifyMobileNo.Text.Trim();
            if (mobileNo.Length < 9)
            {
                SetValidationError(TxtVerifyMobileNo);
                CustomDialog.ShowWarning(
                    "Invalid Mobile Number",
                    "Please enter a valid mobile number with at least 9 digits.",
                    "OK, I'll Fix It");
                return;
            }

            string icType = ExtractComboBoxText(CmbVerifyICType);
            string icNumber = TxtVerifyICNumber.Text.Trim();
            string nationality = ExtractComboBoxText(CmbVerifyNationality);

            var existingSender = _dataService.FindSenderByICAndMobile(icType, icNumber, mobileNo);

            if (existingSender != null)
            {
                // EXISTING CUSTOMER
                _verifiedMobileNo = mobileNo;
                string maskedMobile = mobileNo.Length >= 4
                    ? "******" + mobileNo.Substring(mobileNo.Length - 4)
                    : "******" + mobileNo;

                TxtOTPSentMessage.Text = $"OTP sent to your mobile no {maskedMobile}";

                CustomDialog.ShowSuccess(
                    $"Welcome Back! {existingSender.FullName} 👋",
                    $"We found your profile is Existing already:\n\n" +
                    $"For your security, we've sent a One-Time Password (OTP) to your registered mobile number ending in {maskedMobile.Substring(maskedMobile.Length - 4)}.\n\n" +
                    $"Please check your phone and enter the 6-digit OTP to continue.",
                    "Continue To Enter OTP");

                _vm.SelectedSender = existingSender;

                IdentityVerificationPanel.Visibility = Visibility.Collapsed;
                OTPVerificationPanel.Visibility = Visibility.Visible;
                CreateSenderPanel.Visibility = Visibility.Collapsed;

                ShowNotification(
                    "OTP Sent Successfully",
                    $"Please check your mobile {maskedMobile} for the verification code",
                    "success");
            }
            else
            {
                // NEW CUSTOMER
                CustomDialog.ShowInfo(
                    "New Customer 🎉",
                    "Welcome to our service!\n\n" +
                    $"We couldn't find an existing profile with these details:\n\n" +
                    $"• ID Type: {icType}\n" +
                    $"• ID Number: {icNumber}\n" +
                    $"• Nationality: {nationality}\n" +
                    $"• Mobile: {mobileNo}\n\n" +
                    "Please complete your profile to proceed with the transaction. This is a one-time setup and your details will be securely saved for future transactions.",
                    "Continue to Create Profile");

                // Pre-fill the display fields
                TxtDisplayICType.Text = icType;
                TxtDisplayICNumber.Text = icNumber;
                TxtDisplayMobileNo.Text = mobileNo;


                // Pre-select nationality
                foreach (ComboBoxItem item in CmbNationality.Items)
                {
                    if (item.Content is TextBlock tb && tb.Text == nationality)
                    {
                        CmbNationality.SelectedItem = item;
                        break;
                    }
                }

                // Disable verified fields
                CmbVerifyICType.IsEnabled = false;
                TxtVerifyICNumber.IsEnabled = false;
                CmbVerifyNationality.IsEnabled = false;
                TxtVerifyMobileNo.IsEnabled = false;

                IdentityVerificationPanel.Visibility = Visibility.Collapsed;
                OTPVerificationPanel.Visibility = Visibility.Collapsed;
                CreateSenderPanel.Visibility = Visibility.Visible;

                ShowNotification(
                    "Profile Setup Required",
                    "Please complete your profile details to continue",
                    "info");
            }
        }
        //private void VerifyIdentity_Click(object sender, RoutedEventArgs e)
        //{
        //    _sessionManager?.Reset();

        //    // Clear previous validation
        //    ClearValidationStyles();

        //    // Validate inputs with visual feedback
        //    bool hasErrors = false;

        //    if (CmbVerifyICType.SelectedItem == null)
        //    {
        //        SetValidationError(CmbVerifyICType);
        //        hasErrors = true;
        //    }

        //    if (string.IsNullOrWhiteSpace(TxtVerifyICNumber.Text))
        //    {
        //        SetValidationError(TxtVerifyICNumber);
        //        hasErrors = true;
        //    }

        //    if (string.IsNullOrWhiteSpace(TxtVerifyMobileNo.Text))
        //    {
        //        SetValidationError(TxtVerifyMobileNo);
        //        hasErrors = true;
        //    }

        //    if (hasErrors)
        //    {
        //        CustomDialog.ShowWarning(
        //            "Required Fields Missing",
        //            "Please fill in all required fields:\n\n• IC/Passport Type\n• IC/Passport Number\n• Mobile Number",
        //            "OK, I'll Complete");
        //        return;
        //    }

        //    string mobileNo = TxtVerifyMobileNo.Text.Trim();
        //    if (mobileNo.Length < 9)
        //    {
        //        SetValidationError(TxtVerifyMobileNo);
        //        CustomDialog.ShowWarning(
        //            "Invalid Mobile Number",
        //            "Please enter a valid mobile number with at least 9 digits.",
        //            "OK, I'll Fix It");
        //        return;
        //    }

        //    string icType = ExtractComboBoxText(CmbVerifyICType);
        //    string icNumber = TxtVerifyICNumber.Text.Trim();

        //    var existingSender = _dataService.FindSenderByICAndMobile(icType, icNumber, mobileNo);

        //    if (existingSender != null)
        //    {
        //        _verifiedMobileNo = mobileNo;
        //        string maskedMobile = mobileNo.Length >= 4
        //            ? "******" + mobileNo.Substring(mobileNo.Length - 4)
        //            : "******" + mobileNo;

        //        TxtOTPSentMessage.Text = $"OTP sent to your mobile no {maskedMobile}";

        //        CustomDialog.ShowSuccess(
        //            $"Welcome Back! {existingSender.FullName} 👋",
        //            $"We found your profile is Existing already:\n\n" +
        //            $"For your security, we've sent a One-Time Password (OTP) to your registered mobile number ending in {maskedMobile.Substring(maskedMobile.Length - 4)}.\n\n" +
        //            $"Please check your phone and enter the 6-digit OTP to continue.",
        //            "Continue To Enter OTP");

        //        _vm.SelectedSender = existingSender;

        //        IdentityVerificationPanel.Visibility = Visibility.Collapsed;
        //        OTPVerificationPanel.Visibility = Visibility.Visible;
        //        CreateSenderPanel.Visibility = Visibility.Collapsed;

        //        ShowNotification(
        //            "OTP Sent Successfully",
        //            $"Please check your mobile {maskedMobile} for the verification code",
        //            "success");
        //    }
        //    else
        //    {
        //        CustomDialog.ShowInfo(
        //            "New Customer 🎉",
        //            "Welcome to our service!\n\n" +
        //            $"We couldn't find an existing profile with these details:\n\n" +
        //            $"• ID Type: {icType}\n" +
        //            $"• ID Number: {icNumber}\n" +
        //            $"• Mobile: {mobileNo}\n\n" +
        //            "Please complete your profile to proceed with the transaction. This is a one-time setup and your details will be securely saved for future transactions.",
        //            "Continue to Create Profile");

        //        TxtDisplayICType.Text = icType;
        //        TxtDisplayICNumber.Text = icNumber;
        //        TxtDisplayMobileNo.Text = mobileNo;

        //        CmbVerifyICType.IsEnabled = false;
        //        TxtVerifyICNumber.IsEnabled = false;
        //        TxtVerifyMobileNo.IsEnabled = false;

        //        IdentityVerificationPanel.Visibility = Visibility.Collapsed;
        //        OTPVerificationPanel.Visibility = Visibility.Collapsed;
        //        CreateSenderPanel.Visibility = Visibility.Visible;

        //        ShowNotification(
        //            "Profile Setup Required",
        //            "Please complete your profile details to continue",
        //            "info");
        //    }
        //}

        private void VerifyOTP_Click(object sender, RoutedEventArgs e)
        {
            _sessionManager?.Reset();

            string enteredOTP = TxtOTP.Text.Trim();

            if (string.IsNullOrWhiteSpace(enteredOTP))
            {
                SetValidationError(TxtOTP);
                CustomDialog.ShowWarning(
                    "OTP Required",
                    "Please enter the 6-digit OTP sent to your mobile number.",
                    "OK");
                return;
            }

            if (enteredOTP.Length != 6)
            {
                SetValidationError(TxtOTP);
                CustomDialog.ShowWarning(
                    "Invalid OTP Format",
                    "OTP must be exactly 6 digits.\n\nPlease check your mobile and enter the complete code.",
                    "OK, I'll Check");
                return;
            }

            if (enteredOTP == HARDCODED_OTP)
            {
                CustomDialog.ShowSuccess(
                    "Verification Successful! ✓",
                    $"Welcome back, {_vm.SelectedSender.FullName}!\n\n" +
                    $"Your identity has been verified successfully. You can now proceed to select or add a beneficiary for your remittance.",
                    "Continue to Beneficiary");

                ShowNotification(
                    "OTP Verified Successfully",
                    "Proceeding to beneficiary selection...",
                    "success");

                _sessionManager?.Stop();
                _vm.NavigateTo(RemittanceViewModel.NavigationTarget.Beneficiary);
            }
            else
            {
                SetValidationError(TxtOTP);
                CustomDialog.ShowError(
                    "Incorrect OTP ✗",
                    "The OTP you entered is incorrect.\n\n" +
                    $"Please check your mobile and try again. If you didn't receive the OTP, click 'Resend OTP' below.\n\n" +
                    $"Hint: The OTP is a 6-digit code sent to your mobile ending in {_verifiedMobileNo.Substring(_verifiedMobileNo.Length - 4)}.",
                    "Try Again");

                ShowNotification(
                    "Incorrect OTP",
                    "Please check and enter the correct OTP",
                    "error");

                TxtOTP.Clear();
                TxtOTP.Focus();
            }
        }

        private void ResendOTP_Click(object sender, RoutedEventArgs e)
        {
            _sessionManager?.Reset();

            CustomDialog.ShowSuccess(
                "OTP Resent Successfully",
                $"A new 6-digit OTP has been sent to your mobile number ending in {_verifiedMobileNo.Substring(_verifiedMobileNo.Length - 4)}.\n\n" +
                $"Please check your phone and enter the new code.\n\n" +
                $"Note: The previous OTP is now invalid.",
                "OK, Got It");

            ShowNotification(
                "OTP Resent",
                "A new OTP has been sent to your mobile",
                "success");

            TxtOTP.Clear();
            TxtOTP.Focus();
        }

        private void SaveAndContinue_Click(object sender, RoutedEventArgs e)
        {
            _sessionManager?.Reset();

            // Clear previous validation
            ClearValidationStyles();

            bool hasErrors = false;

            // Validate all required fields with visual feedback
            if (string.IsNullOrWhiteSpace(TxtFirstName.Text))
            {
                SetValidationError(TxtFirstName);
                hasErrors = true;
            }

            if (string.IsNullOrWhiteSpace(TxtLastName.Text))
            {
                SetValidationError(TxtLastName);
                hasErrors = true;
            }

            if (DpDOB.SelectedDate == null)
            {
                SetValidationError(DpDOB);
                hasErrors = true;
            }

            if (CmbNationality.SelectedItem == null)
            {
                SetValidationError(CmbNationality);
                hasErrors = true;
            }

            if (CmbOccupation.SelectedItem == null)
            {
                SetValidationError(CmbOccupation);
                hasErrors = true;
            }

            if (string.IsNullOrWhiteSpace(TxtAddress.Text))
            {
                SetValidationError(TxtAddress);
                hasErrors = true;
            }

            if (string.IsNullOrWhiteSpace(TxtCity.Text))
            {
                SetValidationError(TxtCity);
                hasErrors = true;
            }

            if (string.IsNullOrWhiteSpace(TxtPostcode.Text))
            {
                SetValidationError(TxtPostcode);
                hasErrors = true;
            }

            if (CmbCountry.SelectedItem == null)
            {
                SetValidationError(CmbCountry);
                hasErrors = true;
            }

            if (CmbState.SelectedItem == null)
            {
                SetValidationError(CmbState);
                hasErrors = true;
            }

            if (hasErrors)
            {
                CustomDialog.ShowWarning(
                    "Required Fields Missing",
                    "Please fill in all required fields marked with * before saving.\n\n" +
                    "The fields with red borders need your attention.",
                    "OK, I'll Complete");
                return;
            }

            // Age validation
            if (DpDOB.SelectedDate != null)
            {
                DateTime dob = DpDOB.SelectedDate.Value;
                int age = DateTime.Today.Year - dob.Year;

                if (dob > DateTime.Today.AddYears(-age))
                    age--;

                if (age < 18)
                {
                    SetValidationError(DpDOB);
                    CustomDialog.ShowWarning(
                        "Invalid Age",
                        "The Customer Age Cannot Be Less than 18 Years",
                        "OK");
                    return;
                }
            }

            string icType = ExtractComboBoxText(CmbVerifyICType);
            string nationality = ExtractComboBoxText(CmbNationality);
            string occupation = ExtractComboBoxText(CmbOccupation);
            string country = ExtractComboBoxText(CmbCountry);
            string state = ExtractComboBoxText(CmbState);

            _vm.SenderFullName = TxtFirstName.Text.Trim() + " " + TxtLastName.Text.Trim();
            _vm.SenderIC = TxtVerifyICNumber.Text.Trim();
            _vm.SenderICType = icType;
            _vm.SenderMobileNo = TxtVerifyMobileNo.Text.Trim();
            _vm.SenderDOB = DpDOB.SelectedDate;
            _vm.SenderNationality = nationality;
            _vm.SenderOccupation = occupation;
            _vm.SenderAddress = TxtAddress.Text.Trim();
            _vm.SenderCity = TxtCity.Text.Trim();
            _vm.SenderPostcode = TxtPostcode.Text.Trim();
            _vm.SenderCountry = country;
            _vm.SenderState = state;
            _vm.SenderPhoto = _capturedPhotoData;

            try
            {
                _vm.SaveCurrentSender();

                CustomDialog.ShowSuccess(
                    "Profile Created Successfully 🎉",
                    $"Welcome to our service, {_vm.SenderFullName}!\n\n" +
                    $"Your profile has been created and saved securely. You can now proceed to add a beneficiary and complete your remittance transaction.\n\n" +
                    $"For future transactions, you can simply verify your identity with ID and Mobile number - no need to re-enter all details!",
                    "Continue");

                ShowNotification(
                    "Profile Saved Successfully",
                    $"Welcome, {_vm.SenderFullName}!",
                    "success");

                _sessionManager?.Stop();
                _vm.NavigateTo(RemittanceViewModel.NavigationTarget.Beneficiary);
            }
            catch (Exception ex)
            {
                CustomDialog.ShowError(
                    "Save Error",
                    $"Unable to save sender profile.\n\n" +
                    $"Error: {ex.Message}\n\n" +
                    $"Please try again or contact support if the problem persists.",
                    "Close");
            }
        }

        private void CapturePhoto_Click(object sender, RoutedEventArgs e)
        {
            _sessionManager?.Reset();

            try
            {
                var cameraWindow = new CameraWindow();
                cameraWindow.Owner = Window.GetWindow(this);

                if (cameraWindow.ShowDialog() == true)
                {
                    _capturedPhotoData = cameraWindow.CapturedImageData;

                    if (_capturedPhotoData != null && _capturedPhotoData.Length > 0)
                    {
                        using (var ms = new MemoryStream(_capturedPhotoData))
                        {
                            var image = new BitmapImage();
                            image.BeginInit();
                            image.CacheOption = BitmapCacheOption.OnLoad;
                            image.StreamSource = ms;
                            image.EndInit();
                            image.Freeze();

                            PhotoPreviewImage.Source = image;
                        }

                        CapturedPhotoPreview.Visibility = Visibility.Visible;
                        PhotoPlaceholderIcon.Visibility = Visibility.Collapsed;

                        ShowNotification(
                            "Photo Captured",
                            "Customer photo has been captured successfully",
                            "success");
                    }
                }
            }
            catch (Exception ex)
            {
                CustomDialog.ShowError(
                    "Camera Error",
                    $"Unable to access camera.\n\n" +
                    $"Error: {ex.Message}\n\n" +
                    $"Please ensure:\n" +
                    $"• Camera is connected\n" +
                    $"• Camera permissions are granted\n" +
                    $"• No other application is using the camera",
                    "Close");
            }
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
            else if (control is DatePicker datePicker)
            {
                datePicker.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                datePicker.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEF2F2"));
            }
        }

        private void ClearValidationStyles()
        {
            // Clear all validation styles
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
                else if (control is DatePicker datePicker)
                {
                    datePicker.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0"));
                    datePicker.Background = Brushes.White;
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

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            bool confirmBack = CustomDialog.ShowQuestion(
                "Confirm Navigation",
                "Are you sure you want to go back?\n\nAny unsaved information will be lost.",
                "Yes, Go Back",
                "No, Stay Here");

            if (confirmBack)
            {
                _sessionManager?.Stop();
                _vm.NavigateTo(RemittanceViewModel.NavigationTarget.Back);
            }
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

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
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
            TxtCustomerInfo.Text = $"👤 {LocalizationManager.GetString("CustomerInformation")}";
            TxtEnterDetails.Text = LocalizationManager.GetString("EnterDetailsVerify");

            // Identity Panel
            TxtIdentityDetails.Text = LocalizationManager.GetString("IdentityDetails");
            TxtProvideDetails.Text = LocalizationManager.GetString("ProvideIDContact");

            // Labels - Identity Section
            LblICType.Text = LocalizationManager.GetString("ICPassportType") + " *";
            LblICNumber.Text = LocalizationManager.GetString("ICPassportNumber") + " *";
            LblMobileNumber.Text = LocalizationManager.GetString("MobileNumber") + " *";
            LblNationality.Text = LocalizationManager.GetString("Nationality") + " *";

            // Buttons
            //TxtProceed.Text = LocalizationManager.GetString("Proceed");
            //TxtBack.Text = LocalizationManager.GetString("Back");
            SaveSenderButton.Content = LocalizationManager.GetString("SaveSenderDetails");
            ProceedButton.Content = LocalizationManager.GetString("Proceed");
            // Back Button
            BackButton.Content = LocalizationManager.GetString("Back");

            VerifyOTPButton.Content = LocalizationManager.GetString("VerifyOTP");

            // OTP Panel
            TxtOTPVerification.Text = LocalizationManager.GetString("OTPVerification");
            TxtOTPInfo1.Text = $"✓ {LocalizationManager.GetString("OTPSentInfo")}";
            TxtOTPInfo2.Text = LocalizationManager.GetString("EnterOTPToProceed");
            LblEnterOTP.Text = LocalizationManager.GetString("EnterOTP") + " *";
            //TxtVerifyOTP.Text = LocalizationManager.GetString("VerifyOTP");
            TxtDidntReceive.Text = LocalizationManager.GetString("DidntReceiveOTP");
            TxtResendOTP.Text = LocalizationManager.GetString("ResendOTP");

            // Create Sender Panel
            TxtEnterSenderDetails.Text = LocalizationManager.GetString("EnterSenderDetails");
            TxtCompleteFields.Text = LocalizationManager.GetString("CompleteRequiredFields");
            TxtProfileInfo.Text = LocalizationManager.GetString("ProfileInformation");

            // Profile Fields
            LblFirstName.Text = LocalizationManager.GetString("FirstName");
            LblLastName.Text = LocalizationManager.GetString("LastName");
            LblICTypeDisplay.Text = LocalizationManager.GetString("ICPassportType");
            LblICNumberDisplay.Text = LocalizationManager.GetString("ICPassportNumber");
            LblDOB.Text = LocalizationManager.GetString("DateOfBirth");
            LblMobileNumberDisplay.Text = LocalizationManager.GetString("MobileNumber");
            LblNationalityProfile.Text = LocalizationManager.GetString("Nationality");
            LblOccupation.Text = LocalizationManager.GetString("Occupation");

            // Address Section
            TxtAddressInfo.Text = LocalizationManager.GetString("AddressInformation");
            LblAddress.Text = LocalizationManager.GetString("Address") + " *";
            LblCity.Text = LocalizationManager.GetString("City") + " *";
            LblPostcode.Text = LocalizationManager.GetString("Postcode") + " *";
            LblCountry.Text = LocalizationManager.GetString("Country") + " *";
            LblState.Text = LocalizationManager.GetString("StateProvince") + " *";

            // Photo Section
            LblSenderPhoto.Text = LocalizationManager.GetString("SenderPhoto");
            LblPhotoDesc.Text = LocalizationManager.GetString("CapturePhotoDesc");
            //TxtCapturePhoto.Text = LocalizationManager.GetString("CapturePhoto");

            ////// Save Button
            //TxtSaveSender.Text = LocalizationManager.GetString("SaveSenderDetails");
        }
    }
}