using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Globalization;
using System.Resources;
using OmniKiosk.Wpf.Services.Remittance;
using OmniKiosk.Wpf.Models;
using OmniKiosk.Wpf.Services;

namespace OmniKiosk.Wpf.ViewModels.Remittance
{
    public class RemittanceViewModel : INotifyPropertyChanged
    {
        private readonly RateService _rateService;
        private readonly LocalStorageService _storageService;
        private readonly PrinterService _printerService;
        private readonly DataStorageService _dataStorageService;
        private ResourceManager _resourceManager;

        // Language
        private string _currentLanguage = "en";

        // Navigation event
        public event EventHandler<NavigationRequestedEventArgs> OnRequestNavigate;

        // Selected models
        private SenderModel _selectedSender;
        private BeneficiaryModel _selectedBeneficiary;

        // Customer ID (NEW)
        public string CustomerIdNo { get; set; }

        // Sender Information
        private string _senderFullName;
        private string _senderIC;
        private string _senderICType;
        private DateTime? _senderDOB;
        private string _senderNationality;
        private string _senderOccupation;
        private string _senderEmployer;
        private string _senderAddress;
        private string _senderCity;
        private string _senderPostcode;
        private string _senderState;
        private string _senderCountry = "Malaysia";
        private string _senderMobileNo;
        private string _senderEmail;
        private byte[] _senderPhoto;
        private string _sourceOfFunds;

        // Beneficiary Information
        private string _beneficiaryFullName;
        private string _beneficiaryAccountNo;
        private string _beneficiaryBankName;
        private string _beneficiaryBankCode;
        private string _beneficiaryAddress;
        private string _beneficiaryCity;
        private string _beneficiaryCountry;
        private string _DestinationCountry;
        private string _beneficiaryMobileNo;
        private string _beneficiaryRelationship;
        private string _beneficiaryNationality;

        // Transaction Information
        private string _destinationCountryCode;
        private decimal _sendAmountMYR;
        private decimal _serviceFee;
        private decimal _totalAmountMYR;
        private decimal _exchangeRate;
        private decimal _receiveAmount;
        private string _receiveCurrency;
        private string _purposeOfRemittance;
        private string _deliveryMethod;
        private string _deliveryMethodCode;
        private DateTime _transactionDate = DateTime.Now;
        private string _transactionReference;
        private string _paymentMethod;

        public RemittanceViewModel()
        {
            _rateService = new RateService();
            _storageService = new LocalStorageService();
            _printerService = new PrinterService("POS80");
            _dataStorageService = new DataStorageService();
            _resourceManager = new ResourceManager("OmniKiosk.Wpf.Resources.Strings", typeof(RemittanceViewModel).Assembly);

            GenerateTransactionReference();
        }

        #region Language Properties

        public string CurrentLanguage
        {
            get => _currentLanguage;
            set
            {
                _currentLanguage = value;
                OnPropertyChanged();
                UpdateLanguage(value);
            }
        }

        private void UpdateLanguage(string langCode)
        {
            try
            {
                var culture = new CultureInfo(langCode);
                System.Threading.Thread.CurrentThread.CurrentUICulture = culture;
                System.Threading.Thread.CurrentThread.CurrentCulture = culture;

                OnPropertyChanged(nameof(WelcomeText));
                OnPropertyChanged(nameof(SelectCountryText));
            }
            catch { }
        }

        public string WelcomeText => GetString("WelcomeText") ?? "Welcome to OmniRemit";
        public string SelectCountryText => GetString("SelectCountry") ?? "Select Destination Country";

        private string GetString(string key)
        {
            try
            {
                return _resourceManager.GetString(key);
            }
            catch
            {
                return key;
            }
        }

        #endregion

        #region Sender Properties

        public SenderModel SelectedSender
        {
            get => _selectedSender;
            set
            {
                _selectedSender = value;
                OnPropertyChanged();
                if (value != null)
                    LoadSenderData(value);
            }
        }

        private void LoadSenderData(SenderModel sender)
        {
            SenderFullName = sender.FullName;
            SenderIC = sender.ICNumber;
            SenderICType = sender.ICType;
            SenderDOB = sender.DateOfBirth;
            SenderNationality = sender.Nationality;
            SenderMobileNo = sender.MobileNo;
            SenderEmail = sender.Email;
            SenderAddress = sender.Address;
            SenderCity = sender.City;
            SenderPostcode = sender.Postcode;
            SenderState = sender.State;
            SenderCountry = sender.Country;
            SenderOccupation = sender.Occupation;
            SenderEmployer = sender.Employer;
            SourceOfFunds = sender.SourceOfFunds;
            SenderPhoto = sender.Photo;

            // Set CustomerIdNo from sender's IC
            CustomerIdNo = sender.ICNumber;
        }

        public string SenderFullName
        {
            get => _senderFullName;
            set { _senderFullName = value; OnPropertyChanged(); }
        }

        public string SenderIC
        {
            get => _senderIC;
            set { _senderIC = value; OnPropertyChanged(); }
        }

        public string SenderICType
        {
            get => _senderICType;
            set { _senderICType = value; OnPropertyChanged(); }
        }

        public DateTime? SenderDOB
        {
            get => _senderDOB;
            set { _senderDOB = value; OnPropertyChanged(); }
        }

        public string SenderNationality
        {
            get => _senderNationality;
            set { _senderNationality = value; OnPropertyChanged(); }
        }

        public string SenderOccupation
        {
            get => _senderOccupation;
            set { _senderOccupation = value; OnPropertyChanged(); }
        }

        public string SenderEmployer
        {
            get => _senderEmployer;
            set { _senderEmployer = value; OnPropertyChanged(); }
        }

        public string SenderAddress
        {
            get => _senderAddress;
            set { _senderAddress = value; OnPropertyChanged(); }
        }

        public string SenderCity
        {
            get => _senderCity;
            set { _senderCity = value; OnPropertyChanged(); }
        }

        public string SenderPostcode
        {
            get => _senderPostcode;
            set { _senderPostcode = value; OnPropertyChanged(); }
        }

        public string SenderState
        {
            get => _senderState;
            set { _senderState = value; OnPropertyChanged(); }
        }

        public string SenderCountry
        {
            get => _senderCountry;
            set { _senderCountry = value; OnPropertyChanged(); }
        }

        public string SenderMobileNo
        {
            get => _senderMobileNo;
            set { _senderMobileNo = value; OnPropertyChanged(); }
        }

        public string SenderEmail
        {
            get => _senderEmail;
            set { _senderEmail = value; OnPropertyChanged(); }
        }

        public byte[] SenderPhoto
        {
            get => _senderPhoto;
            set { _senderPhoto = value; OnPropertyChanged(); }
        }

        public string SourceOfFunds
        {
            get => _sourceOfFunds;
            set { _sourceOfFunds = value; OnPropertyChanged(); }
        }

        #endregion

        #region Beneficiary Properties

        public BeneficiaryModel SelectedBeneficiary
        {
            get => _selectedBeneficiary;
            set
            {
                _selectedBeneficiary = value;
                OnPropertyChanged();
                if (value != null)
                    LoadBeneficiaryData(value);
            }
        }

        private void LoadBeneficiaryData(BeneficiaryModel beneficiary)
        {
            BeneficiaryFullName = beneficiary.FullName;
            BeneficiaryCountry = beneficiary.Country;
            BeneficiaryMobileNo = beneficiary.MobileNo;
            BeneficiaryNationality = beneficiary.Nationality;
            BeneficiaryAddress = beneficiary.Address;
            BeneficiaryCity = beneficiary.City;
            BeneficiaryBankName = beneficiary.BankName;
            BeneficiaryBankCode = beneficiary.BankCode;
            BeneficiaryAccountNo = beneficiary.AccountNo;
            BeneficiaryRelationship = beneficiary.Relationship;
        }

        public string BeneficiaryFullName
        {
            get => _beneficiaryFullName;
            set { _beneficiaryFullName = value; OnPropertyChanged(); }
        }

        public string BeneficiaryAccountNo
        {
            get => _beneficiaryAccountNo;
            set { _beneficiaryAccountNo = value; OnPropertyChanged(); }
        }

        public string BeneficiaryBankName
        {
            get => _beneficiaryBankName;
            set { _beneficiaryBankName = value; OnPropertyChanged(); }
        }

        public string BeneficiaryBankCode
        {
            get => _beneficiaryBankCode;
            set { _beneficiaryBankCode = value; OnPropertyChanged(); }
        }

        public string BeneficiaryAddress
        {
            get => _beneficiaryAddress;
            set { _beneficiaryAddress = value; OnPropertyChanged(); }
        }

        public string BeneficiaryCity
        {
            get => _beneficiaryCity;
            set { _beneficiaryCity = value; OnPropertyChanged(); }
        }

        public string BeneficiaryCountry
        {
            get => _beneficiaryCountry;
            set { _beneficiaryCountry = value; OnPropertyChanged(); }
        }
        public string DestinationCountry
        {
            get => _DestinationCountry;
            set { _DestinationCountry = value; OnPropertyChanged(); }
        }

        public string BeneficiaryMobileNo
        {
            get => _beneficiaryMobileNo;
            set { _beneficiaryMobileNo = value; OnPropertyChanged(); }
        }

        public string BeneficiaryRelationship
        {
            get => _beneficiaryRelationship;
            set { _beneficiaryRelationship = value; OnPropertyChanged(); }
        }

        public string BeneficiaryNationality
        {
            get => _beneficiaryNationality;
            set { _beneficiaryNationality = value; OnPropertyChanged(); }
        }

        #endregion

        #region Transaction Properties

        public string DestinationCountryCode
        {
            get => _destinationCountryCode;
            set
            {
                _destinationCountryCode = value;
                OnPropertyChanged();
                CalculateTransaction();
            }
        }

        public decimal SendAmountMYR
        {
            get => _sendAmountMYR;
            set
            {
                _sendAmountMYR = value;
                OnPropertyChanged();
                CalculateTransaction();
            }
        }

        public decimal ServiceFee
        {
            get => _serviceFee;
            set { _serviceFee = value; OnPropertyChanged(); }
        }

        public decimal TotalAmountMYR
        {
            get => _totalAmountMYR;
            set { _totalAmountMYR = value; OnPropertyChanged(); }
        }

        public decimal ExchangeRate
        {
            get => _exchangeRate;
            set { _exchangeRate = value; OnPropertyChanged(); }
        }

        public decimal ReceiveAmount
        {
            get => _receiveAmount;
            set { _receiveAmount = value; OnPropertyChanged(); }
        }

        public string ReceiveCurrency
        {
            get => _receiveCurrency;
            set { _receiveCurrency = value; OnPropertyChanged(); }
        }

        public string PurposeOfRemittance
        {
            get => _purposeOfRemittance;
            set { _purposeOfRemittance = value; OnPropertyChanged(); }
        }

        public string DeliveryMethod
        {
            get => _deliveryMethod;
            set { _deliveryMethod = value; OnPropertyChanged(); }
        }

        public string DeliveryMethodCode
        {
            get => _deliveryMethodCode;
            set { _deliveryMethodCode = value; OnPropertyChanged(); }
        }

        public DateTime TransactionDate
        {
            get => _transactionDate;
            set { _transactionDate = value; OnPropertyChanged(); }
        }

        public string TransactionReference
        {
            get => _transactionReference;
            set { _transactionReference = value; OnPropertyChanged(); }
        }

        public string PaymentMethod
        {
            get => _paymentMethod;
            set { _paymentMethod = value; OnPropertyChanged(); }
        }

        #endregion

        #region Methods

        private void CalculateTransaction()
        {
            if (string.IsNullOrEmpty(DestinationCountryCode) || SendAmountMYR <= 0)
                return;

            var calculation = _rateService.CalculateTransaction(DestinationCountryCode, SendAmountMYR);

            ServiceFee = calculation.ServiceFee;
            TotalAmountMYR = calculation.TotalCostMYR;
            ExchangeRate = calculation.ExchangeRate;
            ReceiveAmount = calculation.ReceiveAmount;
            ReceiveCurrency = calculation.ReceiveCurrency;
        }

        public ValidationResult ValidateTransaction()
        {
            return _rateService.ValidateTransactionAmount(SendAmountMYR);
        }

        private void GenerateTransactionReference()
        {
            TransactionReference = $"RMT{DateTime.Now:yyyyMMddHHmmss}{new Random().Next(1000, 9999)}";
        }

        public void SaveCurrentSender()
        {
            var sender = new SenderModel
            {
                FullName = SenderFullName,
                ICNumber = SenderIC,
                ICType = SenderICType,
                DateOfBirth = SenderDOB ?? DateTime.Now,
                Nationality = SenderNationality,
                MobileNo = SenderMobileNo,
                Email = SenderEmail,
                Address = SenderAddress,
                City = SenderCity,
                Postcode = SenderPostcode,
                State = SenderState,
                Country = SenderCountry,
                Occupation = SenderOccupation,
                Employer = SenderEmployer,
                SourceOfFunds = SourceOfFunds,
                Photo = SenderPhoto
            };

            _dataStorageService.SaveSender(sender);

            // Set CustomerIdNo after saving
            CustomerIdNo = SenderIC;
        }

        public void SaveCurrentBeneficiary()
        {
            // Initialize SelectedBeneficiary if null
            if (SelectedBeneficiary == null)
            {
                SelectedBeneficiary = new BeneficiaryModel();
            }

            var beneficiary = new BeneficiaryModel
            {
                CustomerIdNo = this.CustomerIdNo, // Link to customer
                FullName = BeneficiaryFullName,
                FirstName = BeneficiaryFullName?.Split(' ').FirstOrDefault() ?? "",
                LastName = string.Join(" ", BeneficiaryFullName?.Split(' ').Skip(1) ?? new string[] { }),
                Country = BeneficiaryCountry,
                CountryCode = DestinationCountryCode,
                MobileNo = BeneficiaryMobileNo,
                Nationality = BeneficiaryNationality,
                Address = BeneficiaryAddress,
                City = BeneficiaryCity,
                BankName = BeneficiaryBankName,
                BankCode = BeneficiaryBankCode,
                AccountNo = BeneficiaryAccountNo,
                Relationship = BeneficiaryRelationship,

                // Copy BNM fields from SelectedBeneficiary
                IFSC = SelectedBeneficiary.IFSC,
                RoutingNumber = SelectedBeneficiary.RoutingNumber,
                SwiftCode = SelectedBeneficiary.SwiftCode,
                IBAN = SelectedBeneficiary.IBAN
            };

            _dataStorageService.SaveBeneficiary(beneficiary);

            // Update SelectedBeneficiary reference
            SelectedBeneficiary = beneficiary;
        }

        public void PrintReceipt()
        {
            var receipt = GenerateReceiptText();
            _printerService.PrintTextSilent(receipt);
        }

        private string GenerateReceiptText()
        {
            string formattedRate = _rateService.FormatExchangeRate(ExchangeRate);
            return $@"
========================================
       OMNIREMIT MONEY TRANSFER
========================================
Transaction Ref: {TransactionReference}
Date: {TransactionDate:dd/MM/yyyy HH:mm}

SENDER INFORMATION
Name: {SenderFullName}
IC/Passport: {SenderIC}
Mobile: {SenderMobileNo}

BENEFICIARY INFORMATION
Name: {BeneficiaryFullName}
Country: {BeneficiaryCountry}
Account: {BeneficiaryAccountNo}
Bank: {BeneficiaryBankName}

TRANSACTION DETAILS
Send Amount:     {SendAmountMYR:N2} MYR
Service Fee:     {ServiceFee:N2} MYR
Total Paid:      {TotalAmountMYR:N2} MYR

Exchange Rate:   {formattedRate}
Receive Amount:  {ReceiveCurrency} {ReceiveAmount:N2}

Purpose: {PurposeOfRemittance}
Delivery: {DeliveryMethod}

========================================
Thank you for choosing OmniRemit!
========================================
";
        }

        public void NavigateTo(NavigationTarget target)
        {
            OnRequestNavigate?.Invoke(this, new NavigationRequestedEventArgs { Target = target });
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion

        #region Navigation

        public enum NavigationTarget
        {
            CDD,
            Sender,
            Beneficiary,
            Summary,
            Back
        }

        public class NavigationRequestedEventArgs : EventArgs
        {
            public NavigationTarget Target { get; set; }
        }

        #endregion
    }
}