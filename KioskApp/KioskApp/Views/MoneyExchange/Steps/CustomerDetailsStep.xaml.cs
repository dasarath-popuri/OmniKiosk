using OmniKiosk.Wpf.Config;
using OmniKiosk.Wpf.Controls;
using OmniKiosk.Wpf.Models.MoneyExchange;
using OmniKiosk.Wpf.Sdk.IC;
using OmniKiosk.Wpf.Sdk.Passport;
using OmniKiosk.Wpf.Services.MoneyExchange;
using OmniKiosk.Wpf.Services; // GlobalManager
using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
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
        private readonly MoneyExchangeApiClient _api = new();

        // Document TYPE the customer chose, independent of nationality - a
        // Malaysian can hold a passport too, so this is no longer inferred
        // from a nationality pick.
        private string _selectedDocType = "IC";

        public CustomerDetailsStep(MoneyExchangeFlowController ctl)
        {
            InitializeComponent();
            _ctl = ctl;
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            TitleText.Text = L10n.T("Mx_CustomerDetails", "Identity Verification");
            SubtitleText.Text = L10n.T("Mx_SelectDocSubtitle", "Please select the document you have with you.");
            IcTitle.Text = L10n.T("Mx_IcDocTitle", "MyKad / IC");
            IcSubtitle.Text = L10n.T("Mx_ReadIC", "Read IC");
            PassportTitle.Text = L10n.T("Mx_PassportDocTitle", "Passport");
            PassportSubtitle.Text = L10n.T("Mx_ScanPassport", "Scan Passport");
            IdentityConfirmedText.Text = L10n.T("Mx_IdentityConfirmed", "Identity Confirmed");
            DocNoLabel.Text = L10n.T("Mx_DocumentNo", "DOCUMENT NO.");
            NationalityLabel.Text = L10n.T("Mx_NationalityLabel", "NATIONALITY");
            GenderLabel.Text = L10n.T("Mx_GenderLabel", "GENDER");
            DobLabel.Text = L10n.T("Mx_DobLabel", "DATE OF BIRTH");
            ExpiryLabel.Text = L10n.T("Mx_ExpiryLabel", "EXPIRY DATE");
            MobileLabel.Text = L10n.T("Mx_MobileNo", "MOBILE NUMBER");
            MobileHintText.Text = L10n.T("Mx_MobileHint", "We'll use this to speed up your next visit");
            TxtMobile.Text = "";
            BtnBack.Content = L10n.T("Mx_Back", "Back");
            BtnNext.Content = L10n.T("Mx_VerifyFace", "Verify Face ➔");

            ShowView("Selection");
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            StopPassportLoop();
            StopScanAnimations();
        }

        private void ShowView(string viewName)
        {
            ViewSelection.Visibility = Visibility.Collapsed;
            ViewScanning.Visibility = Visibility.Collapsed;
            ViewResult.Visibility = Visibility.Collapsed;
            BtnNext.Visibility = Visibility.Collapsed;

            if (viewName == "Selection")
            {
                SubtitleText.Text = L10n.T("Mx_SelectDocSubtitle", "Please select the document you have with you.");
                ViewSelection.Visibility = Visibility.Visible;
            }
            else if (viewName == "Scanning") ViewScanning.Visibility = Visibility.Visible;
            else if (viewName == "Result") { ViewResult.Visibility = Visibility.Visible; BtnNext.Visibility = Visibility.Visible; }
        }

        private void StopScanAnimations()
        {
            PassportAnimStage.Visibility = Visibility.Collapsed;
            CardAnimStage.Visibility = Visibility.Collapsed;
            (this.Resources["PassportPlaceAnim"] as Storyboard)?.Stop(this);
            (this.Resources["CardInsertAnim"] as Storyboard)?.Stop(this);
        }

        private async void BtnIc_Click(object sender, MouseButtonEventArgs e)
        {
            _selectedDocType = "IC";
            SubtitleText.Text = L10n.T("Mx_ReadingGuidance", "Please follow the instructions below to complete the reading.");
            AutoReadStatus.Text = L10n.T("Mx_InsertMyKad", "Please insert your MyKad into the reader");
            StatusText.Text = "";

            StopScanAnimations();
            CardAnimStage.Visibility = Visibility.Visible;
            (this.Resources["CardInsertAnim"] as Storyboard)?.Begin(this, true);

            ShowView("Scanning");
            await StartIcScanAsync();
        }

        private void BtnPassport_Click(object sender, MouseButtonEventArgs e)
        {
            _selectedDocType = "Passport";
            SubtitleText.Text = L10n.T("Mx_ReadingGuidance", "Please follow the instructions below to complete the reading.");
            AutoReadStatus.Text = L10n.T("Mx_PlacePassport", "Please place your passport face-down on the scanner");
            StatusText.Text = "";

            StopScanAnimations();
            PassportAnimStage.Visibility = Visibility.Visible;
            (this.Resources["PassportPlaceAnim"] as Storyboard)?.Begin(this, true);

            ShowView("Scanning");
            StartPassportScan();
        }

        private async Task StartIcScanAsync()
        {
            StatusText.Text = L10n.T("Mx_ReadingMyKad", "Reading MyKad securely...");
            if (_icSvc == null) { StatusText.Text = L10n.T("Mx_IcOffline", "IC Reader offline."); return; }

            var result = await _icSvc.ReadCardAsync();
            if (result.Data != null)
            {
                if (_ctl.State.Customer == null) _ctl.State.Customer = new CustomerProfile();
                var cust = _ctl.State.Customer;

                cust.FullName = result.Data.FullName;
                cust.IdNo = result.Data.IdNumber;
                cust.Nationality = result.Data.Nationality;
                cust.Sex = result.Data.Gender;
                cust.DateOfBirth = result.Data.DateOfBirth;
                cust.DateOfExpiry = null; // MyKad has no fixed expiry the same way a passport does

                if (result.Data.PhotoBytes != null)
                {
                    try
                    {
                        using var ms = new MemoryStream(result.Data.PhotoBytes);
                        var bmp = new BitmapImage(); bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.StreamSource = ms; bmp.EndInit();
                        PortraitImage.Source = bmp;
                        cust.FaceImageBase64 = Convert.ToBase64String(result.Data.PhotoBytes);
                    }
                    catch { }
                }

                StopScanAnimations();
                PopulateResultView();
                ShowView("Result");
                UpdateNextEnabled();
            }
            else { StatusText.Text = L10n.T("Mx_HardwareReadFailed", "Hardware Read Failed. Please go back and try again."); }
        }

        private void StartPassportScan()
        {
            if (_svc == null)
            {
                StatusText.Text = L10n.T("Mx_ScannerMissing", "Passport Scanner missing or failed to boot.");
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
                            if (_ctl.State.Customer == null) _ctl.State.Customer = new CustomerProfile();
                            var cust = _ctl.State.Customer;

                            cust.FullName = doc.FullName ?? "";
                            cust.IdNo = doc.PassportNumber ?? "";
                            cust.Nationality = doc.Nationality ?? "";
                            cust.Sex = doc.Sex ?? "";
                            cust.DateOfBirth = doc.DateOfBirth ?? "";
                            cust.DateOfExpiry = doc.DateOfExpiry;

                            if (!string.IsNullOrWhiteSpace(portraitPath) && File.Exists(portraitPath))
                            {
                                var bmp = new BitmapImage(); bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.UriSource = new Uri(portraitPath); bmp.EndInit();
                                PortraitImage.Source = bmp;
                                byte[] imageBytes = File.ReadAllBytes(portraitPath);
                                cust.FaceImageBase64 = Convert.ToBase64String(imageBytes);
                            }

                            StopScanAnimations();

                            // Block here, before ever reaching the result screen,
                            // if the passport is expired - not something we
                            // proceed past regardless of what else reads fine.
                            if (IsExpired(cust.DateOfExpiry, out var expiryDisplay))
                            {
                                ShowView("Selection");
                                CustomDialog.ShowError(
                                    L10n.T("Mx_PassportExpiredTitle", "Passport Expired"),
                                    string.Format(L10n.T("Mx_PassportExpiredBody", "This passport expired on {0} and cannot be used for this transaction. Please use a valid, unexpired document."), expiryDisplay));
                                return;
                            }

                            PopulateResultView();
                            ShowView("Result");
                            UpdateNextEnabled();
                        });
                        StopPassportLoop(); break;
                    }
                }
                catch { Thread.Sleep(500); }
            }
        }

        // Tries several date formats since the exact one the SDK returns
        // hasn't been confirmed against live hardware - if none of them parse,
        // this deliberately does NOT wave the document through. An unreadable
        // expiry date fails safe (treated as expired, sent back to try again
        // or see staff) rather than silently skipping the compliance check.
        private static readonly string[] SdkDateFormats =
        {
            "dd/MM/yyyy", "yyyy-MM-dd", "yyyyMMdd", "dd-MM-yyyy", "MM/dd/yyyy", "yyMMdd"
        };

        private static bool IsExpired(string? dateOfExpiry, out string displayValue)
        {
            displayValue = dateOfExpiry ?? "";
            if (string.IsNullOrWhiteSpace(dateOfExpiry)) return false; // nothing to check (e.g. MyKad)

            DateTime parsed;
            bool ok = DateTime.TryParseExact(dateOfExpiry, SdkDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed);
            if (!ok) ok = DateTime.TryParse(dateOfExpiry, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed);

            if (!ok)
            {
                // Could not confirm the date is valid - fail safe rather than
                // silently let an unverifiable document through.
                return true;
            }

            displayValue = parsed.ToString("dd MMM yyyy");
            return parsed.Date < DateTime.Today;
        }

        // Same format list as IsExpired above - used for DOB/expiry when
        // sending a new customer's details to the API, which wants a real
        // DateTime, not the raw SDK string. Returns null if unparseable
        // rather than guessing - a null DateOfBirth on a new SenderMaster
        // record is honest; a wrong one is worse than missing.
        private static DateTime? TryParseSdkDate(string? sdkDate)
        {
            if (string.IsNullOrWhiteSpace(sdkDate)) return null;

            DateTime parsed;
            bool ok = DateTime.TryParseExact(sdkDate, SdkDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed);
            if (!ok) ok = DateTime.TryParse(sdkDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed);

            return ok ? parsed : null;
        }

        private void PopulateResultView()
        {
            var cust = _ctl.State.Customer;
            if (cust == null) return;

            TxtName.Text = cust.FullName;
            TxtIdNo.Text = cust.IdNo;
            TxtNat.Text = cust.Nationality;
            TxtGender.Text = string.IsNullOrWhiteSpace(cust.Sex) ? "-" : cust.Sex;
            TxtDob.Text = string.IsNullOrWhiteSpace(cust.DateOfBirth) ? "-" : cust.DateOfBirth;

            if (!string.IsNullOrWhiteSpace(cust.DateOfExpiry))
            {
                IsExpired(cust.DateOfExpiry, out var display);
                TxtExpiry.Text = display;
                ExpiryPanel.Visibility = Visibility.Visible;
            }
            else
            {
                ExpiryPanel.Visibility = Visibility.Collapsed;
            }
        }

        // IMPORTANT: Only cancel the token. DO NOT dispose the global _svc!
        private void StopPassportLoop() { try { _cts?.Cancel(); } catch { } }

        private void TxtMobile_TextChanged(object sender, TextChangedEventArgs e) => UpdateNextEnabled();

        // Next stays disabled until a mobile number is entered, alongside a
        // successful scan - matches "save mobile number along with the other
        // customer details" from the spec, since the SDKs don't supply one.
        private void UpdateNextEnabled()
        {
            BtnNext.IsEnabled = ViewResult.Visibility == Visibility.Visible
                && !string.IsNullOrWhiteSpace(TxtMobile.Text)
                && TxtMobile.Text.Trim().Length >= 7;
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (ViewScanning.Visibility == Visibility.Visible || ViewResult.Visibility == Visibility.Visible)
            {
                StopPassportLoop();
                StopScanAnimations();
                ShowView("Selection");
            }
            else BackRequested?.Invoke(this, EventArgs.Empty);
        }

        private async void Next_Click(object sender, RoutedEventArgs e)
        {
            if (_ctl.State.Customer == null) _ctl.State.Customer = new CustomerProfile();
            _ctl.State.Customer.IdType = _selectedDocType;
            _ctl.State.Customer.IdNo = TxtIdNo.Text;
            _ctl.State.Customer.FullName = TxtName.Text;
            _ctl.State.Customer.Nationality = TxtNat.Text;
            _ctl.State.Customer.MobileNo = TxtMobile.Text.Trim();

            // Local upsert still happens - this is what caches a face-match
            // feature for fast local re-verification on a future visit, and
            // sets State.IsExistingCustomer as a first pass. The central
            // check below is what actually decides, at the business level,
            // whether this is a known customer - it overrides the local
            // result rather than working alongside it.
            _ctl.UpsertCustomer(_ctl.State.Customer);

            BtnNext.IsEnabled = false;
            StatusText.Text = L10n.T("Mx_CheckingCustomer", "Checking customer record…");

            try
            {
                var result = await _api.CheckCustomerAsync(_selectedDocType, TxtIdNo.Text);

                if (result.IsBlocked == true)
                {
                    BtnNext.IsEnabled = true;
                    StatusText.Text = "";
                    CustomDialog.ShowError(
                        L10n.T("Mx_CustomerBlockedTitle", "Unable to Proceed"),
                        L10n.T("Mx_CustomerBlockedBody", "This transaction cannot be completed at this kiosk. Please see a member of staff for assistance."));
                    return;
                }

                _ctl.State.IsExistingCustomer = result.Found;
                _ctl.State.SenderId = result.SenderId;

                // New customer - no SenderMaster row exists yet. Create one
                // now so screening (and the transaction's CustomerRef later)
                // has a real SenderId to attach to, the same as an existing
                // customer already has.
                //
                // *** FALLBACK - see the note at the top of Ksk_CreateNewSender.sql.
                // If a real sender-creation proc already exists in this system,
                // this call should be replaced with that instead of the raw
                // INSERT this currently triggers. ***
                if (!result.Found)
                {
                    try
                    {
                        var newSenderId = await _api.CreateCustomerAsync(new CreateCustomerApiRequest
                        {
                            KioskId = "K1", // TODO: real kiosk identity once machine auth exists
                            IdType = _selectedDocType,
                            IdNo = TxtIdNo.Text,
                            FullName = TxtName.Text,
                            Nationality = TxtNat.Text,
                            DateOfBirth = TryParseSdkDate(_ctl.State.Customer.DateOfBirth),
                            Gender = _ctl.State.Customer.Sex,
                            MobileNo = TxtMobile.Text.Trim(),
                            IdExpiryDate = TryParseSdkDate(_ctl.State.Customer.DateOfExpiry)
                        });

                        if (newSenderId > 0)
                        {
                            _ctl.State.SenderId = newSenderId;
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("[CustomerDetails] CreateCustomer returned no SenderId - screening will be skipped for this customer.");
                        }
                    }
                    catch (Exception ex)
                    {
                        // Could not create the central record - this customer
                        // proceeds without a SenderId, same as the existing
                        // "screening skipped" fallback below, not blocked
                        // outright over a network/API problem.
                        System.Diagnostics.Debug.WriteLine("[CustomerDetails] Failed to create SenderMaster record: " + ex.Message);
                    }
                }

                // Watchlist screening - needs a real SenderMaster.SenderID,
                // which now exists whether this customer was found or just
                // created above.
                if (_ctl.State.SenderId.HasValue)
                {
                    var screening = await _api.ScreenCustomerAsync(_ctl.State.SenderId.Value, _ctl.State.ScreeningTransGuid!);

                    if (screening.HasMatch)
                    {
                        BtnNext.IsEnabled = true;
                        StatusText.Text = "";
                        CustomDialog.ShowError(
                            L10n.T("Mx_ScreeningMatchTitle", "Unable to Proceed at This Kiosk"),
                            L10n.T("Mx_ScreeningMatchBody", "We're unable to complete this transaction here. Please proceed to the counter for assistance."));
                        return;
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[CustomerDetails] No SenderId resolved - watchlist screening skipped for this (new) customer.");
                }
            }
            catch (Exception ex)
            {
                // Central check unreachable - falls back to the local
                // determination already set by UpsertCustomer above rather
                // than blocking the whole transaction on an API outage.
                // Worth knowing this happened, not worth stopping a
                // legitimate customer over a network hiccup.
                System.Diagnostics.Debug.WriteLine("[CustomerDetails] SenderMaster check failed, using local fallback: " + ex.Message);
            }

            BtnNext.IsEnabled = true;
            StatusText.Text = "";

            NextRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
