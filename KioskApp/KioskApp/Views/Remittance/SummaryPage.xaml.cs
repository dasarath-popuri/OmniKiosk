using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using OmniKiosk.Wpf.Controls;
using OmniKiosk.Wpf.ViewModels.Remittance;
using OmniKiosk.Wpf.Services;
using OmniKiosk.Wpf.Services.Remittance;
using OmniKiosk.Wpf.Helpers;

namespace OmniKiosk.Wpf.Views.Remittance
{
    public partial class SummaryPage : Page
    {
        private readonly RemittanceViewModel _vm;
        private Random _random = new Random();
        private PaymentTerminalService _terminalService;
        private SessionTimeoutManager _sessionManager;
        private readonly RateService _rateService;
        private MediaPlayer _welcomePlayer;
        private bool _hasPlayedWelcome = false;



        public event EventHandler RemittanceFlowCompleted;

        public SummaryPage(RemittanceViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            this.DataContext = _vm;

            //this.Loaded += SummaryPage_Loaded;
            this.Loaded += (s, e) => ApplyLocalization();
            //try
            //{
            //    _terminalService = new PaymentTerminalService("COM5");
            //}
            //catch (Exception ex)
            //{
            //    ShowNotification("Terminal Error", $"Failed to initialize terminal: {ex.Message}", "error");
            //}
        }

        //private void SummaryPage_Loaded(object sender, RoutedEventArgs e)
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

        private void PayWithCard_Click(object sender, RoutedEventArgs e)
        {
            _sessionManager?.Reset();

            // Show Terms and Conditions Dialog
            bool termsAccepted = ShowTermsAndConditionsDialog();

            if (!termsAccepted)
            {
                return;
            }

            // Proceed with card payment
            ProcessCardPayment();
        }

        private void PayAtCounter_Click(object sender, RoutedEventArgs e)
        {
            _sessionManager?.Reset();

            // Show Terms and Conditions Dialog
            bool termsAccepted = ShowTermsAndConditionsDialog();

            if (!termsAccepted)
            {
                return;
            }

            // Proceed with counter payment
            bool proceed = CustomDialog.ShowQuestion(
                "Pay at Counter",
                $"Transaction Reference: {_vm.TransactionReference}\nTotal Amount: MYR {_vm.TotalAmountMYR:N2}\n\nWe will print a Booking Slip for you to take to the counter.\n\nReady to proceed?",
                "Yes, Print Booking Slip",
                "No, Cancel");

            if (proceed)
            {
                ProcessCounterPayment();
            }
        }

        //private bool ShowTermsAndConditionsDialog()
        //{
        //    string termsMessage = 
        //                         "Please read and accept the following terms:\n\n" +
        //                         "   • This transaction is subject to Bank Negara Malaysia (BNM) regulations\n" +
        //                         "   • We are required to report suspicious transactions to authorities\n" +
        //                         "   • Transfer completion time varies by destination country (1-3 business days)\n" +
        //                         "   • Delays may occur due to bank holidays or regulatory checks\n" +
        //                         "   • Keep your transaction receipt for your records\n" +
        //                         "By clicking 'I Accept', you confirm that:\n" +
        //                         "• All information provided is true and accurate\n" +
        //                         "• You have read and understood these terms\n" +
        //                         "• You agree to comply with all applicable regulations\n\n" +
        //                         "Do you accept these terms and conditions?";

        //    bool accepted = CustomDialog.ShowQuestion(
        //        "Terms and Conditions",
        //        termsMessage,
        //        "I Accept",
        //        "I Decline");

        //    if (!accepted)
        //    {
        //        ShowNotification(
        //            "Terms Required",
        //            "You must accept the terms and conditions to proceed with the transaction.",
        //            "warning");
        //    }

        //    return accepted;
        //}

        private bool ShowTermsAndConditionsDialog()
        {
            bool accepted = CustomTermsDialog.Show();

            if (!accepted)
            {
                ShowNotification(
                    "Terms Required",
                    "You must accept the terms and conditions to proceed with the transaction.",
                    "warning");
            }

            return accepted;
        }
        private void StartNewTransaction_Click(object sender, RoutedEventArgs e)
        {
            bool confirm = CustomDialog.ShowQuestion(
                "Start New Transaction?",
                "This will clear all current transaction data and start fresh.\n\nAre you sure?",
                "Yes, Start New",
                "No, Cancel");

            if (confirm)
            {
                // Clear all data
                ClearTransactionData();

                // Navigate back to CDD page
                _vm.NavigateTo(RemittanceViewModel.NavigationTarget.CDD);
            }
        }
        private async void ProcessCardPayment()
        {
            try
            {
                PayWithCardButton.IsEnabled = false;
                PayAtCounterButton.IsEnabled = false;

                ShowNotification(
                    "Processing Payment",
                    "Please follow instructions on the card reader...",
                    "info");

                string transactionId = DateTime.Now.ToString("yyyyMMddHHmmssfff");
                decimal amount = _vm.TotalAmountMYR;

                System.Diagnostics.Debug.WriteLine($"Starting payment - TxnID: {transactionId}, Amount: MYR {amount:N2}");

                var response = await _terminalService.ProcessPurchase(transactionId, amount, merchantIndex: 1);

                System.Diagnostics.Debug.WriteLine($"Payment response received - Approved: {response.IsApproved}, Code: {response.ResponseCode}");

                if (response.IsApproved && response.ResponseCode == "00")
                {
                    HandleSuccessfulPayment(response);
                }
                else
                {
                    HandleFailedPayment(response);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Terminal processing error: {ex.Message}\n{ex.StackTrace}");

                ShowNotification(
                    "Transaction Failed",
                    "Payment could not be completed. Please try again.",
                    "error");

                CustomDialog.ShowError(
                    "Transaction Error",
                    $"Payment terminal error:\n\n{ex.Message}\n\nPlease try again or pay at counter.",
                    "OK");

                PayWithCardButton.IsEnabled = true;
                PayAtCounterButton.IsEnabled = true;
            }
        }

        private void HandleSuccessfulPayment(PaymentResponse response)
        {
            System.Diagnostics.Debug.WriteLine("Payment approved - Processing successful transaction");

            ShowNotification(
                "Payment Approved!",
                "Transaction completed successfully",
                "success");

            string receiptNo = GenerateReceiptNumber();
            PrintCardPaymentReceiptWithTerminalDetails(receiptNo, response);

            CustomDialog.ShowSuccess(
                "Payment Successful!",
                $"✓ Transaction Completed\n\n" +
                $"Receipt No: {receiptNo}\n" +
                $"Approval Code: {response.ApprovalCode}\n" +
                $"Card: {response.CardLabel} {response.CardNumber}\n" +
                $"Amount: MYR {decimal.Parse(response.Amount) / 100:F2}\n\n" +
                $"Your receipt has been printed.\n" +
                $"The money will be transferred to the beneficiary within 1-2 business days.\n\n" +
                $"Thank you for using OmniRemit!",
                "Done");

            SaveTransactionToDatabase(response, receiptNo);

            PayWithCardButton.IsEnabled = true;
            PayAtCounterButton.IsEnabled = true;

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                _sessionManager?.Stop();
                OnRemittanceFlowCompleted();
            };
            timer.Start();
        }

        private void HandleFailedPayment(PaymentResponse response)
        {
            System.Diagnostics.Debug.WriteLine($"Payment declined - Code: {response.ResponseCode}, Message: {response.ResponseText}");

            ShowNotification(
                "Payment Declined",
                response.ResponseText ?? "Transaction was not approved",
                "error");

            string errorMessage = GetUserFriendlyErrorMessage(response.ResponseCode, response.ResponseText);

            CustomDialog.ShowError(
                "Payment Declined",
                $"{errorMessage}\n\n" +
                $"Response: {response.ResponseText}\n" +
                $"Code: {response.ResponseCode}\n\n" +
                $"Please try:\n" +
                $"• Using a different card\n" +
                $"• Contacting your bank\n" +
                $"• Pay at counter instead",
                "OK");

            PayWithCardButton.IsEnabled = true;
            PayAtCounterButton.IsEnabled = true;
        }

        private string GetUserFriendlyErrorMessage(string responseCode, string responseText)
        {
            switch (responseCode)
            {
                case "51":
                    return "Insufficient funds in your account.";
                case "05":
                    return "Transaction declined by your bank.";
                case "54":
                    return "Your card has expired.";
                case "57":
                    return "This transaction is not permitted with your card.";
                case "75":
                    return "PIN entry limit exceeded.";
                case "91":
                    return "Network error. Please try again.";
                case "TO":
                    return "Transaction timeout. Please try again.";
                case "UA":
                    return "Transaction cancelled by user.";
                default:
                    return responseText ?? "Transaction could not be completed.";
            }
        }

        private void SaveTransactionToDatabase(PaymentResponse response, string receiptNo)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Saving transaction to database:");
                System.Diagnostics.Debug.WriteLine($"  Receipt No: {receiptNo}");
                System.Diagnostics.Debug.WriteLine($"  Transaction ID: {response.TransactionID}");
                System.Diagnostics.Debug.WriteLine($"  Approval Code: {response.ApprovalCode}");
                System.Diagnostics.Debug.WriteLine($"  Amount: {response.Amount}");
                System.Diagnostics.Debug.WriteLine($"  Card: {response.CardLabel} {response.CardNumber}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving to database: {ex.Message}");
            }
        }

        private void PrintCardPaymentReceiptWithTerminalDetails(string receiptNo, PaymentResponse response)
        {
            decimal paidAmount = 0;
            if (!string.IsNullOrEmpty(response.Amount))
            {
                paidAmount = decimal.Parse(response.Amount) / 100;
            }
            string formattedRate = _rateService.FormatExchangeRate(_vm.ExchangeRate);
            string cardEntryMethod = GetCardEntryMethodDescription(response.CardEntryMode);

            string receipt = $@"
========================================
          RM Applications
========================================
         PAYMENT RECEIPT (CARD)
========================================

Receipt No: {receiptNo}
Date & Time: {DateTime.Now:dd/MM/yyyy HH:mm:ss}
Transaction Ref: {_vm.TransactionReference}

========================================
           SENDER INFORMATION
========================================
Name       : {_vm.SenderFullName}
IC/Passport: {_vm.SenderIC}
Mobile     : {_vm.SenderMobileNo}

========================================
        BENEFICIARY INFORMATION
========================================
Name       : {_vm.BeneficiaryFullName}
Country    : {_vm.BeneficiaryCountry}
Mobile     : {_vm.BeneficiaryMobileNo}
Bank       : {_vm.BeneficiaryBankName}
Account No : {_vm.BeneficiaryAccountNo}
Relationship: {_vm.BeneficiaryRelationship}

========================================
         TRANSACTION DETAILS
========================================
Send Amount    : MYR {_vm.SendAmountMYR:N2}
Service Fee    : MYR {_vm.ServiceFee:N2}
                 ----------
Total Paid     : MYR {paidAmount:N2}

Exchange Rate  : {formattedRate}
Receive Amount : {_vm.ReceiveCurrency} {_vm.ReceiveAmount:N2}

Purpose        : {_vm.PurposeOfRemittance}
Delivery Method: {_vm.DeliveryMethod}

========================================
          PAYMENT INFORMATION
========================================
Payment Method : CARD PAYMENT
Payment Status : PAID
Card Type      : {response.CardLabel}
Card Number    : {response.CardNumber}
Entry Method   : {cardEntryMethod}
Authorization  : {response.ApprovalCode}
Terminal ID    : {response.TerminalID}
Merchant No    : {response.MerchantNumber}
Invoice No     : {response.InvoiceNumber}
Trace No       : {response.TraceNumber}
Batch No       : {response.BatchNumber}
Date/Time      : {response.TransactionDate} {response.TransactionTime}

{response.ReceiptFooterCustomer}

========================================
         IMPORTANT INFORMATION
========================================
* Transfer will be completed within
  1-3 business days
* Keep this receipt for your records
* For inquiries, contact our hotline
  at 1-300-88-REMIT
* Transaction Reference: {response.TransactionID}
========================================

       THANK YOU FOR YOUR BUSINESS
========================================
";

            try
            {
                var printerService = new OmniKiosk.Wpf.Services.Remittance.PrinterService("POS80");
                printerService.PrintText(receipt);
                System.Diagnostics.Debug.WriteLine("Receipt printed successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Printer error: {ex.Message}");
                ShowNotification("Printer Issue", $"Receipt data saved but printer error: {ex.Message}", "warning");
            }
        }

        private string GetCardEntryMethodDescription(string cardEntryMode)
        {
            if (string.IsNullOrEmpty(cardEntryMode))
                return "Unknown";

            switch (cardEntryMode)
            {
                case "10":
                    return "Chip Card";
                case "20":
                    return "Contactless";
                case "30":
                    return "Magnetic Stripe";
                case "40":
                    return "Fallback";
                case "50":
                    return "Manual Entry";
                case "60":
                    return "QR Code";
                default:
                    return "Unknown";
            }
        }

        private void ProcessCounterPayment()
        {
            int counterNo = _random.Next(1, 6);
            PrintCounterPaymentReceipt(counterNo);
            PrintPage_ThankyouSound();

            CustomDialog.ShowSuccess(
                "Booking Successful",
                $"Booking has been printed successfully!\n\nPlease proceed to Counter\n\nShow this receipt to the staff to complete your payment.\n\nPayment Options Available:\n• Cash\n• Debit/Credit Card\n• E-Wallet (Touch 'n Go, GrabPay)",
                "Got it");

            _sessionManager?.Stop();
            OnRemittanceFlowCompleted();
        }
        private void PrintPage_ThankyouSound()
        {
            if (!_hasPlayedWelcome)
            {
                // Delay slightly to ensure UI is fully loaded
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                timer.Tick += (s, ev) =>
                {
                    timer.Stop();
                    PlayThanksSound();
                };
                timer.Start();
                _hasPlayedWelcome = true;
            }
        }
        private void PlayThanksSound()
        {
            try
            {
                // Initialize MediaPlayer
                _welcomePlayer = new MediaPlayer();

                // CORRECTED PATH - Relative to your bin output directory
                string baseDir = System.AppDomain.CurrentDomain.BaseDirectory;
                string audioPath = System.IO.Path.Combine(baseDir, "Assets", "Audio", "thanks.mp3");


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
                    string sourceAudioPath = System.IO.Path.Combine(projectRoot, "Assets", "Audio", "thanks.mp3");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"✗ EXCEPTION: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack: {ex.StackTrace}");
            }
        }

        protected virtual void OnRemittanceFlowCompleted()
        {
            RemittanceFlowCompleted?.Invoke(this, EventArgs.Empty);
            CloseRemittanceFlow();
        }

        private void CloseRemittanceFlow()
        {
            try
            {
                if (_vm != null)
                {
                    ClearTransactionData();
                }

                var parentWindow = Window.GetWindow(this);

                if (parentWindow is RemittanceMainWindow remittanceWindow)
                {
                    remittanceWindow.DialogResult = true;
                    remittanceWindow.Close();
                }
                else
                {
                    if (this.NavigationService != null)
                    {
                        while (this.NavigationService.CanGoBack)
                        {
                            this.NavigationService.RemoveBackEntry();
                        }

                        this.NavigationService.Navigate(new Uri("Views/MainMenuPage.xaml", UriKind.Relative));
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error closing remittance flow: {ex.Message}");
            }
        }

        private void ClearTransactionData()
        {
            _vm.SendAmountMYR = 0;
            _vm.ServiceFee = 0;
            _vm.TotalAmountMYR = 0;
            _vm.ReceiveAmount = 0;
            _vm.DestinationCountryCode = null;
            _vm.SelectedSender = null;
            _vm.SelectedBeneficiary = null;
            _vm.PurposeOfRemittance = null;
            _vm.DeliveryMethod = null;
            _vm.SourceOfFunds = null;
        }

        private string GenerateReceiptNumber()
        {
            return $"RCP{DateTime.Now:yyyyMMdd}{_random.Next(100, 999)}";
        }

        private void PrintCounterPaymentReceipt(int counterNo)
        {
            string maskedID = _vm.SenderIC.Length >= 4
                   ? "******" + _vm.SenderIC.Substring(_vm.SenderIC.Length - 4)
                   : "******" + _vm.SenderIC;

            string receipt = $@"
========================================
           RM APPLICATIONS
========================================
           BOOKING RECEIPT
========================================
Transaction Ref: {_vm.TransactionReference}
Date & Time: {DateTime.Now:dd/MM/yyyy HH:mm:ss}

   *** PLEASE PROCEED TO COUNTER ***
========================================
           SENDER INFORMATION
========================================
Name       : {_vm.SenderFullName}
IC/Passport: {maskedID}
Mobile     : {_vm.SenderMobileNo}
========================================
        BENEFICIARY INFORMATION
========================================
Name       : {_vm.BeneficiaryFullName}
Country    : {_vm.DestinationCountry}
========================================
         PAYMENT DETAILS
========================================
Send Amount    : MYR {_vm.SendAmountMYR:N2}
Service Fee    : MYR  {_vm.ServiceFee:N2}
                 ----------
AMOUNT TO PAY  : MYR {_vm.TotalAmountMYR:N2}

Exchange Rate  : {_vm.ExchangeRate:F4}
Bene Amount    : {_vm.ReceiveCurrency} {_vm.ReceiveAmount:N2}
========================================
      PAYMENT INSTRUCTIONS
========================================
1. Please Proceed to Counter and Show this receipt
to staff to Complete your Transaction
2. Payment Options at Counter:
   - Cash
   - Debit/Credit Card
   - E-Wallet (Touch 'n Go, GrabPay)
========================================
           IMPORTANT NOTES
========================================
* Transfer will be processed within
  1-3 business days after payment
========================================

            THANK YOU
========================================
";

            try
            {
                var printerService = new OmniKiosk.Wpf.Services.Remittance.PrinterService("POS80");
                printerService.PrintText(receipt);
            }
            catch (Exception ex)
            {
                ShowNotification("Printer Issue", $"Receipt preview ready. Printer error: {ex.Message}", "warning");
            }
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            _sessionManager?.Stop();
            _vm.NavigateTo(RemittanceViewModel.NavigationTarget.Back);
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

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            _terminalService?.CloseConnection();
            _sessionManager?.Stop();
        }
        private void ApplyLocalization()
        {
            // Header
            TxtReviewTransaction.Text = $"📋 {LocalizationManager.GetString("ReviewTransaction")}";
            TxtReviewCarefully.Text = LocalizationManager.GetString("ReviewCarefully");

            // Amount Summary
            TxtYouSendSummary.Text = LocalizationManager.GetString("YouSend");
            TxtBeneReceivesSummary.Text = LocalizationManager.GetString("BeneficiaryReceives");
            TxtServiceFeeSummary.Text = LocalizationManager.GetString("ServiceFee");
            TxtTotalAmountSummary.Text = LocalizationManager.GetString("TotalAmount");
            TxtRateLabel.Text = LocalizationManager.GetString("Rate");

            // Sender Card
            TxtSenderDetails.Text = LocalizationManager.GetString("SenderDetails");
            TxtFullNameSender.Text = LocalizationManager.GetString("FullName");
            TxtICPassportSender.Text = LocalizationManager.GetString("ICPassport");
            TxtMobileSender.Text = LocalizationManager.GetString("Mobile");
            TxtOccupationSender.Text = LocalizationManager.GetString("Occupation");
            TxtAddressSender.Text = LocalizationManager.GetString("Address");

            // Beneficiary Card
            TxtBeneficiaryDetails.Text = LocalizationManager.GetString("BeneficiaryDetails");
            TxtFullNameBene.Text = LocalizationManager.GetString("FullName");
            TxtRelationshipBene.Text = LocalizationManager.GetString("Relationship");
            TxtMobileBene.Text = LocalizationManager.GetString("Mobile");
            TxtCountryBene.Text = LocalizationManager.GetString("Country");
            TxtBankDetailsBene.Text = LocalizationManager.GetString("BankDetails");
            TxtPurposeBene.Text = LocalizationManager.GetString("Purpose");
            TxtDeliveryBene.Text = LocalizationManager.GetString("Delivery");

            // Payment Method
            TxtSelectPayment.Text = LocalizationManager.GetString("SelectPaymentMethod");
            TxtChoosePayment.Text = LocalizationManager.GetString("ChoosePayment");
            PayWithCardButton.Tag = LocalizationManager.GetString("PayWithCard");
            PayAtCounterButton.Tag = LocalizationManager.GetString("PayAtCounter");

            // Navigation Buttons - Update Content property
            BackButton.Content = LocalizationManager.GetString("Back");
            StartNewButton.Content = LocalizationManager.GetString("StartNewTransaction");
        }
    }
}