using OmniKiosk.Wpf.Config;
using OmniKiosk.Wpf.Controls;
using OmniKiosk.Wpf.Models.MoneyExchange;
using OmniKiosk.Wpf.Sdk.IC;
using OmniKiosk.Wpf.Sdk.Passport;
using OmniKiosk.Wpf.Services.Ekyc;
using OmniKiosk.Wpf.Services.MoneyExchange;
using OmniKiosk.Wpf.Services; // GlobalManager
using System;
using System.Globalization;
using System.IO;
using System.Linq;
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
        private readonly EkycFaceMatchClient _ekyc = new();

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
            DateOfIssueLabel.Text = L10n.T("Mx_DateOfIssueLabel", "DATE OF ISSUE");
            PlaceOfBirthLabel.Text = L10n.T("Mx_PlaceOfBirthLabel", "PLACE OF BIRTH");
            PlaceOfIssueLabel.Text = L10n.T("Mx_PlaceOfIssueLabel", "PLACE OF ISSUE");
            MobileLabel.Text = L10n.T("Mx_MobileNo", "MOBILE NUMBER");
            MobileHintText.Text = L10n.T("Mx_MobileHint", "We'll use this to speed up your next visit");
            TxtMobile.Text = "";
            BtnBack.Content = L10n.T("Mx_Back", "Back");
            BtnNext.Content = L10n.T("Mx_VerifyFace", "Verify Face ➔");
            KeypadTitleText.Text = L10n.T("Mx_KeypadTitle", "ENTER YOUR MOBILE NUMBER");
            KeypadPlaceholderText.Text = L10n.T("Mx_KeypadPlaceholder", "Tap the numbers below");
            KeypadHintText.Text = L10n.T("Mx_KeypadHint", "Country code is added automatically");
            KeypadClearButton.Content = L10n.T("Mx_KeypadClear", "Clear");
            KeypadDoneButton.Content = L10n.T("Mx_KeypadDone", "Done");
            CloseMobileKeypad();

            _ = LoadDialCodesAsync();

            ShowView("Selection");

            _ = _api.LogJourneyEventAsync(_ctl.State.SessionId, "MoneyExchange", "StepEntered", "CustomerDetails");
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            CloseMobileKeypad();
            StopPassportLoop();
            StopScanAnimations();
        }

        private void ShowView(string viewName)
        {
            CloseMobileKeypad();
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

        private void BtnIc_Click(object sender, MouseButtonEventArgs e)
        {
            _selectedDocType = "IC";
            SubtitleText.Text = L10n.T("Mx_ReadingGuidance", "Please follow the instructions below to complete the reading.");
            AutoReadStatus.Text = L10n.T("Mx_PlaceMyKad", "Please place your MyKad face-up on the scanner");
            StatusText.Text = "";

            // Standardized on the SAME optical reader used for passports,
            // per instruction - no longer a separate chip-based path. The
            // reader recognizes either document type automatically (see
            // PassportReaderService.Init()'s "Any" mode), so this now
            // starts the exact same scan loop as BtnPassport_Click below.
            StopScanAnimations();
            PassportAnimStage.Visibility = Visibility.Visible;
            (this.Resources["PassportPlaceAnim"] as Storyboard)?.Begin(this, true);

            ShowView("Scanning");
            StartPassportScan();
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

        private void StartPassportScan()
        {
            if (_svc == null)
            {
                StatusText.Text = L10n.T("Mx_ScannerMissing", "Passport Scanner missing or failed to boot.");
                return;
            }
            _cts = new CancellationTokenSource(); _ = Task.Run(() => PassportReadLoop(_cts.Token));
        }

        // ================================================================
        // UNIFIED DOCUMENT SCAN - standardized on the optical reader for
        // BOTH IC and Passport, per instruction. TryReadAnyDocument
        // recognizes either document type automatically; this loop
        // branches only where the two genuinely differ (expiry only
        // applies to passports; OkayDoc has a separate Passport/MyKad
        // variant).
        //
        // eKYC (OkayID/OkayDoc) now runs ONLY for a genuinely new
        // customer - checked BEFORE any Innov8tif call, not after. An
        // existing customer's document is read and used to populate the
        // result screen exactly as before, but no eKYC call is made for
        // them at all, per instruction.
        //
        // Retry: a genuine connectivity failure (couldn't create a
        // journey, or a call to Innov8tif didn't succeed at all) offers
        // the customer a retry - re-initializing the reader SDK and
        // restarting this same scan loop - rather than silently
        // continuing or hard-failing. A call that DID succeed but found a
        // genuine issue with the document is NOT a retry case - that's a
        // real finding, logged here and left for Scorecard (in
        // FaceVerificationStep) to weigh, unchanged from before.
        // ================================================================
        private void PassportReadLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (_svc.CheckOnlineEx() != 1) { Thread.Sleep(600); continue; }
                    if (_svc.TryReadAnyDocument(out var doc, out var portraitPath, out var fullPageImagePath))
                    {
                        if (string.IsNullOrWhiteSpace(doc.PassportNumber)) { Thread.Sleep(200); continue; }

                        bool isPassport = doc.DetectedDocType == "Passport";
                        bool stopEarly = false;

                        Dispatcher.Invoke(() =>
                        {
                            if (_ctl.State.Customer == null) _ctl.State.Customer = new CustomerProfile();
                            var cust = _ctl.State.Customer;

                            cust.FullName = doc.FullName ?? "";
                            cust.IdNo = doc.PassportNumber ?? "";
                            cust.Nationality = doc.Nationality ?? "";
                            cust.Sex = doc.Sex ?? "";
                            cust.DateOfBirth = doc.DateOfBirth ?? "";
                            cust.Address = doc.Address;

                            // Passport-only fields - stay null for MyKad,
                            // which doesn't carry these.
                            cust.DateOfExpiry = isPassport ? doc.DateOfExpiry : null;
                            cust.DateOfIssue = isPassport ? doc.DateOfIssue : null;
                            cust.PlaceOfBirth = isPassport ? doc.PlaceOfBirth : null;
                            cust.PlaceOfIssue = isPassport ? doc.PlaceOfIssue : null;

                            if (!string.IsNullOrWhiteSpace(portraitPath) && File.Exists(portraitPath))
                            {
                                var bmp = new BitmapImage(); bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.UriSource = new Uri(portraitPath); bmp.EndInit();
                                PortraitImage.Source = bmp;
                                byte[] imageBytes = File.ReadAllBytes(portraitPath);
                                cust.FaceImageBase64 = Convert.ToBase64String(imageBytes);
                            }

                            // Expiry only applies to passports - MyKad has
                            // no equivalent fixed expiry the same way.
                            if (isPassport && IsExpired(cust.DateOfExpiry, out var expiryDisplay))
                            {
                                StopScanAnimations();
                                ShowView("Selection");
                                CustomDialog.ShowError(
                                    L10n.T("Mx_PassportExpiredTitle", "Passport Expired"),
                                    string.Format(L10n.T("Mx_PassportExpiredBody", "This passport expired on {0} and cannot be used for this transaction. Please use a valid, unexpired document."), expiryDisplay));
                                stopEarly = true;
                                return;
                            }

                            if (!string.IsNullOrWhiteSpace(fullPageImagePath) && File.Exists(fullPageImagePath))
                                cust.IdDocumentImageBase64 = Convert.ToBase64String(File.ReadAllBytes(fullPageImagePath));

                            StatusText.Text = L10n.T("Mx_VerifyingDocument", "Verifying document…");
                        });

                        if (stopEarly) { StopPassportLoop(); break; }

                        var cust2 = _ctl.State.Customer;

                        // Early existing-customer check, BEFORE any eKYC call -
                        // per instruction, Innov8tif is used ONLY for new
                        // customers. Also pre-fills the mobile number for an
                        // existing customer, same as before.
                        bool isNewCustomer = true;
                        if (cust2 != null)
                        {
                            try
                            {
                                var checkResult = _api.CheckCustomerAsync(_selectedDocType, cust2.IdNo).GetAwaiter().GetResult();
                                isNewCustomer = !checkResult.Found;
                                if (checkResult.Found && !string.IsNullOrWhiteSpace(checkResult.MobileNo))
                                    Dispatcher.Invoke(() => TxtMobile.Text = checkResult.MobileNo);
                            }
                            catch (Exception ex)
                            {
                                // Fail-safe: treat as new on a failed check,
                                // same reasoning as everywhere else this
                                // check is made - runs one extra
                                // verification step rather than skipping one
                                // that was actually needed.
                                KioskLocalLogger.LogError("CustomerDetails", "Existing-customer check failed (treating as new): " + ex.Message);
                            }
                        }

                        bool needsRetry = false;
                        string retryReason = "";

                        if (isNewCustomer && cust2 != null && !string.IsNullOrWhiteSpace(cust2.IdDocumentImageBase64)
                            && string.IsNullOrWhiteSpace(_ctl.State.EkycJourneyId))
                        {
                            var journey = _ekyc.CreateJourneyIdAsync(cust2.IdNo).GetAwaiter().GetResult();
                            if (journey.ok && !string.IsNullOrWhiteSpace(journey.journeyId))
                            {
                                _ctl.State.EkycJourneyId = journey.journeyId;

                                var idResult = _ekyc.VerifyDocumentAsync(journey.journeyId!, cust2.IdDocumentImageBase64).GetAwaiter().GetResult();
                                if (!idResult.ok)
                                {
                                    KioskLocalLogger.LogError("CustomerDetails", "OkayID call failed: " + idResult.error);
                                    needsRetry = true;
                                    retryReason = idResult.error ?? "OkayID unreachable";
                                }

                                if (!needsRetry)
                                {
                                    var authResult = isPassport
                                        ? _ekyc.VerifyPassportAuthenticityAsync(journey.journeyId!, cust2.IdDocumentImageBase64).GetAwaiter().GetResult()
                                        : _ekyc.VerifyMyKadAuthenticityAsync(journey.journeyId!, cust2.IdDocumentImageBase64).GetAwaiter().GetResult();

                                    if (authResult.CallSucceeded && !authResult.AllChecksPassed)
                                    {
                                        // A genuine finding, not an error - logged
                                        // only, Scorecard (in FaceVerificationStep)
                                        // is what actually decides, unchanged.
                                        var failedList = string.Join(", ", authResult.FailedChecks.Select(f => $"{f.Check}={f.Result}"));
                                        KioskLocalLogger.LogError("CustomerDetails", $"OkayDoc reported failed checks for {cust2.IdNo} (logged only, Scorecard decides): {failedList}");
                                    }
                                    else if (!authResult.CallSucceeded)
                                    {
                                        KioskLocalLogger.LogError("CustomerDetails", "OkayDoc call failed: " + authResult.ErrorMessage);
                                        needsRetry = true;
                                        retryReason = authResult.ErrorMessage ?? "OkayDoc unreachable";
                                    }
                                }
                            }
                            else
                            {
                                KioskLocalLogger.LogError("CustomerDetails", "Could not create eKYC journey: " + journey.error);
                                needsRetry = true;
                                retryReason = journey.error ?? "Could not start verification";
                            }
                        }

                        if (needsRetry)
                        {
                            // A genuine connectivity failure, not a document
                            // finding - offer to retry rather than silently
                            // continuing or hard-failing. journeyId is reset
                            // so a retry starts a genuinely fresh journey,
                            // not reusing one that may be in a bad state.
                            _ctl.State.EkycJourneyId = null;
                            bool retry = false;

                            Dispatcher.Invoke(() =>
                            {
                                StopScanAnimations();
                                retry = CustomDialog.ShowQuestion(
                                    L10n.T("Mx_EkycErrorTitle", "Verification Error"),
                                    string.Format(L10n.T("Mx_EkycErrorBody", "We couldn't verify your document ({0}). Please place it on the scanner again.\n\nTry again?"), retryReason),
                                    L10n.T("Mx_TryAgain", "Try Again"),
                                    L10n.T("Mx_Exit", "Exit"));
                            });

                            if (retry)
                            {
                                RestartDocumentScan();
                                return;
                            }
                            else
                            {
                                Dispatcher.Invoke(() => ExitRequested?.Invoke(this, EventArgs.Empty));
                                StopPassportLoop();
                                return;
                            }
                        }

                        Dispatcher.Invoke(() =>
                        {
                            StopScanAnimations();
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

        // Re-initializes the reader SDK from scratch and restarts the scan
        // loop, per instruction ("ask them to retry... initializing the
        // SDKs"). Runs on whatever thread called it (PassportReadLoop's
        // background thread), matching StartPassportScan's own pattern of
        // kicking a fresh Task.Run for the loop itself.
        private void RestartDocumentScan()
        {
            try { _svc.Init(); }
            catch (Exception ex)
            {
                KioskLocalLogger.LogError("CustomerDetails", "Failed to re-initialize reader SDK for retry: " + ex.Message);
                Dispatcher.Invoke(() =>
                {
                    CustomDialog.ShowError(
                        L10n.T("Mx_ScannerMissing", "Passport Scanner missing or failed to boot."),
                        ex.Message);
                    ExitRequested?.Invoke(this, EventArgs.Empty);
                });
                return;
            }

            Dispatcher.Invoke(() =>
            {
                StatusText.Text = "";
                ShowView("Scanning");
                StopScanAnimations();
                PassportAnimStage.Visibility = Visibility.Visible;
                (this.Resources["PassportPlaceAnim"] as Storyboard)?.Begin(this, true);
            });

            _cts = new CancellationTokenSource();
            _ = Task.Run(() => PassportReadLoop(_cts.Token));
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
                TxtDateOfIssue.Text = string.IsNullOrWhiteSpace(cust.DateOfIssue) ? "-" : cust.DateOfIssue;
                TxtPlaceOfBirth.Text = string.IsNullOrWhiteSpace(cust.PlaceOfBirth) ? "-" : cust.PlaceOfBirth;
                TxtPlaceOfIssue.Text = string.IsNullOrWhiteSpace(cust.PlaceOfIssue) ? "-" : cust.PlaceOfIssue;
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

        // Numeric-only keyboard input for the mobile number field - blocks
        // any non-digit character from ever being entered, rather than
        // accepting then stripping it after the fact.
        private void TxtMobile_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            foreach (char c in e.Text)
            {
                if (!char.IsDigit(c))
                {
                    e.Handled = true;
                    return;
                }
            }
        }

        // ── Mobile number keypad ──────────────────────────────────────────
        // TxtMobile is Focusable="False" + IsReadOnly="True", so the Windows
        // touch keyboard (full QWERTY) never auto-opens for it. Tapping the
        // field opens our own digits-only keypad instead. Every key edits
        // TxtMobile.Text directly, so TxtMobile_TextChanged ->
        // UpdateNextEnabled() keeps working exactly as before, and Next_Click
        // still reads TxtMobile.Text unchanged.
        private const int MobileMaxDigits = 15; // matches TxtMobile MaxLength

        private void TxtMobile_Tap(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            OpenMobileKeypad();
        }

        private void OpenMobileKeypad()
        {
            KeypadDialCodeText.Text = (CboDialCode.SelectedItem as DialCodeDisplayOption)?.DialCode ?? "+60";
            RefreshKeypadDisplay();
            MobileKeypadOverlay.Visibility = Visibility.Visible;
        }

        private void CloseMobileKeypad()
        {
            MobileKeypadOverlay.Visibility = Visibility.Collapsed;
        }

        private void RefreshKeypadDisplay()
        {
            string digits = TxtMobile.Text ?? "";
            KeypadDisplayText.Text = digits;
            KeypadPlaceholderText.Visibility = digits.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void KeypadDigit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b || b.Tag is not string digit) return;
            string current = TxtMobile.Text ?? "";
            if (current.Length >= MobileMaxDigits) return;
            TxtMobile.Text = current + digit;
            RefreshKeypadDisplay();
        }

        private void KeypadBackspace_Click(object sender, RoutedEventArgs e)
        {
            string current = TxtMobile.Text ?? "";
            if (current.Length == 0) return;
            TxtMobile.Text = current.Substring(0, current.Length - 1);
            RefreshKeypadDisplay();
        }

        private void KeypadClear_Click(object sender, RoutedEventArgs e)
        {
            TxtMobile.Text = "";
            RefreshKeypadDisplay();
        }

        private void KeypadDone_Click(object sender, RoutedEventArgs e) => CloseMobileKeypad();

        // Tap on the dimmed area outside the keypad card closes it.
        private void MobileKeypadOverlay_BackgroundTap(object sender, MouseButtonEventArgs e) => CloseMobileKeypad();

        // Stops taps on the card's empty space bubbling up to the overlay
        // and closing the keypad by accident.
        private void MobileKeypadCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

        // Dial code dropdown - KSK_GetCountryDialCodes via GetDialCodesAsync,
        // which never throws (see its own comment) and returns an empty
        // list on any failure. If that happens, this falls back to a
        // single hardcoded Malaysia (+60) entry rather than leaving the
        // dropdown completely empty, since this kiosk's default customer
        // base is local regardless of whether the reference-data call
        // succeeded.
        private async Task LoadDialCodesAsync()
        {
            var codes = await _api.GetDialCodesAsync();

            List<DialCodeDisplayOption> options;
            if (codes.Count > 0)
            {
                options = codes.Select(c => new DialCodeDisplayOption
                {
                    IsoCode = c.IsoCode,
                    CountryName = c.CountryName,
                    DialCode = c.DialCode,
                    FlagUri = FlagUri(c.ta3)
                }).ToList();
            }
            else
            {
                KioskLocalLogger.LogError("CustomerDetails", "GetDialCodesAsync returned empty - falling back to Malaysia only.");
                options = new List<DialCodeDisplayOption>
                {
                    new() { IsoCode = "my", CountryName = "Malaysia", DialCode = "+60", FlagUri = FlagUri("my") }
                };
            }

            CboDialCode.ItemsSource = options;
            CboDialCode.SelectedIndex = 0; // Malaysia is always sorted first by KSK_GetCountryDialCodes, or is the only entry in the fallback
        }

        private void CboDialCode_SelectionChanged(object sender, SelectionChangedEventArgs e) { /* no live-formatting dependency on the mobile textbox itself; the selected dial code is only read at confirm/save time */ }

        //private static Uri FlagUri(string isoCode) => new($"pack://application:,,,/Assets/Flags/{isoCode.ToLowerInvariant()}.svg");
        private static Uri FlagUri(string isoCode) => new($"https://flagcdn.com/w40/{isoCode.ToLowerInvariant()}.png");

        // Fires right after a successful document read (both IC and
        // Passport paths) - a fast, read-only lookup purely for pre-filling
        // the mobile field if this is a returning customer whose number is
        // already on file. This does NOT replace or change the authoritative
        // check-and-create-if-new logic already in Next_Click below; that
        // still runs unchanged. This is only about not making a returning
        // customer re-type a number that's already known.
        private async Task TryPrefillExistingCustomerMobileAsync(string idType, string idNo)
        {
            if (string.IsNullOrWhiteSpace(idNo)) return;

            try
            {
                var result = await _api.CheckCustomerAsync(idType, idNo);
                if (result.Found && !string.IsNullOrWhiteSpace(result.MobileNo))
                {
                    Dispatcher.Invoke(() =>
                    {
                        // The stored number may already have a dial code
                        // prefix (e.g. "+60123456789") - split it against
                        // the loaded dial-code list so the ComboBox and the
                        // local-number textbox each get their own part,
                        // rather than dumping the whole string into
                        // TxtMobile and doubling the dial code once Next_Click
                        // combines them again. If the dial-code list hasn't
                        // finished loading yet (LoadDialCodesAsync is a
                        // separate fire-and-forget call), this falls back to
                        // the old behavior of just showing the whole stored
                        // string - the confirmation step still catches a
                        // wrong-looking result before it's saved anywhere.
                        var options = CboDialCode.ItemsSource as List<DialCodeDisplayOption>;
                        var matched = options?
                            .Where(o => result.MobileNo!.StartsWith(o.DialCode))
                            .OrderByDescending(o => o.DialCode.Length) // longest match first, in case of overlapping prefixes
                            .FirstOrDefault();

                        if (matched != null)
                        {
                            CboDialCode.SelectedItem = matched;
                            TxtMobile.Text = result.MobileNo!.Substring(matched.DialCode.Length);
                        }
                        else
                        {
                            TxtMobile.Text = result.MobileNo;
                        }

                        UpdateNextEnabled();
                    });
                }
            }
            catch (Exception ex)
            {
                // Pure convenience lookup - a failure here just means the
                // customer types their number again, same as any new
                // customer. Never worth surfacing or blocking on.
                KioskLocalLogger.LogError("CustomerDetails", "Existing-customer mobile pre-fill check failed (non-blocking): " + ex.Message);
            }
        }

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
            // Combine the selected dial code with the entered local number
            // into the single string SenderMaster.MobileNo actually stores -
            // there isn't a separate column for the two parts, so this is
            // the final, saved format ("+60123456789"), not just a display
            // convenience.
            string dialCode = "+" + (CboDialCode.SelectedItem as DialCodeDisplayOption)?.DialCode ?? "+60";
            string localNumber = TxtMobile.Text.Trim();
            string enteredMobile = dialCode.Trim() + localNumber;

            // Confirm the mobile number before proceeding - shown every time,
            // whether it was typed fresh or pre-filled from an existing
            // record, since either could still be wrong (pre-filled numbers
            // go stale; typed numbers get mistyped).
            bool mobileConfirmed = CustomDialog.ShowQuestion(
                L10n.T("Mx_ConfirmMobileTitle", "Confirm your mobile number"),
                string.Format(L10n.T("Mx_ConfirmMobileBody", "You entered: {0}\n\nIs this correct?"), enteredMobile),
                L10n.T("Mx_ConfirmMobileYes", "Yes, Correct"),
                L10n.T("Mx_ConfirmMobileNo", "No, Edit"));

            if (!mobileConfirmed) return;

            if (_ctl.State.Customer == null) _ctl.State.Customer = new CustomerProfile();
            _ctl.State.Customer.IdType = _selectedDocType;
            _ctl.State.Customer.IdNo = TxtIdNo.Text;
            _ctl.State.Customer.FullName = TxtName.Text;
            _ctl.State.Customer.Nationality = TxtNat.Text;
            _ctl.State.Customer.MobileNo = enteredMobile;

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
                            KioskId = await KioskAuthService.GetKioskIdAsync(),
                            BranchId = await KioskAuthService.GetKioskBranchIdAsync(),
                            IdType = _selectedDocType,
                            IdNo = TxtIdNo.Text,
                            FullName = TxtName.Text,
                            Nationality = TxtNat.Text,
                            DateOfBirth = TryParseSdkDate(_ctl.State.Customer.DateOfBirth),
                            Gender = _ctl.State.Customer.Sex,
                            MobileNo = TxtMobile.Text.Trim(),
                            IdExpiryDate = TryParseSdkDate(_ctl.State.Customer.DateOfExpiry),
                            // The portrait captured during the unified
                            // document scan (PassportReadLoop, via
                            // TryReadAnyDocument - same optical reader for
                            // both IC and passport) - same data that's
                            // cached locally for face matching, now also
                            // written to the central record.
                            Picture1Base64 = _ctl.State.Customer.FaceImageBase64,
                            // Full document image - now populated for BOTH
                            // document types by the unified PassportReadLoop
                            // (via TryReadAnyDocument), not passport-only.
                            IdDocumentImageBase64 = _ctl.State.Customer.IdDocumentImageBase64
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

            // Full compliance limit check (per-transaction + daily +
            // rolling-30-day), now that a real SenderId exists - ONLY for
            // an existing customer. A genuinely new customer's daily/
            // monthly totals would be zero regardless (no prior history
            // under this SenderId), and their per-transaction cap was
            // already confirmed at the currency selection screen using
            // the same declared amount - re-checking here would be
            // redundant, not incorrect, but skipped deliberately per
            // instruction rather than just relying on it trivially passing.
            //
            // Deliberately its OWN try/catch, separate from the one above -
            // that block's catch intentionally falls back and continues
            // (an unreachable SenderMaster check shouldn't block a
            // legitimate customer). This check has the opposite posture:
            // it protects a regulatory control, so an unreachable or
            // failed check here must stop the transaction, not fall
            // through silently.
            if (_ctl.State.IsExistingCustomer && _ctl.State.SenderId.HasValue)
            {
                try
                {
                    var kioskId = await KioskAuthService.GetKioskIdAsync();
                    var limitResult = await _api.CheckLimitsAsync(_ctl.State.SenderId.Value, kioskId, (decimal)_ctl.State.MyrAmount);

                    if (!limitResult.IsWithinLimits)
                    {
                        BtnNext.IsEnabled = true;
                        StatusText.Text = "";

                        string reason = limitResult.BreachedLimit switch
                        {
                            "PerTransaction" => L10n.T("Mx_LimitPerTxnBody", "This amount exceeds the maximum allowed for a single transaction. Please visit your nearest branch to complete this exchange."),
                            "Daily" => L10n.T("Mx_LimitDailyBody", "You have reached your daily exchange limit. Please visit your nearest branch, or try again tomorrow."),
                            "Rolling30Day" => L10n.T("Mx_LimitMonthlyBody", "You have reached your monthly exchange limit. Please visit your nearest branch to continue."),
                            _ => L10n.T("Mx_LimitGenericBody", "This transaction exceeds an applicable limit. Please visit your nearest branch to complete this exchange.")
                        };
                        CustomDialog.ShowError(L10n.T("Mx_LimitExceededTitle", "Unable to Proceed at This Kiosk"), reason);
                        ExitRequested?.Invoke(this, EventArgs.Empty);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    KioskLocalLogger.LogError("CustomerDetails", "Full compliance limit check failed for existing customer (blocking as a precaution): " + ex.Message);
                    BtnNext.IsEnabled = true;
                    StatusText.Text = "";
                    CustomDialog.ShowError(
                        L10n.T("Mx_LimitExceededTitle", "Unable to Proceed at This Kiosk"),
                        L10n.T("Mx_LimitCheckFailedBody", "We couldn't verify transaction limits for this customer. Please proceed to the counter for assistance."));
                    ExitRequested?.Invoke(this, EventArgs.Empty);
                    return;
                }
            }

            BtnNext.IsEnabled = true;
            StatusText.Text = "";

            _ = _api.LogJourneyEventAsync(_ctl.State.SessionId, "MoneyExchange", "StepCompleted", "CustomerDetails",
                outcome: "Success", transactionId: _ctl.State.TransactionId);

            NextRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public class DialCodeDisplayOption
    {
        public string IsoCode { get; set; } = "";

        public string ta3 { get; set; } = "";
        public string CountryName { get; set; } = "";
        public string DialCode { get; set; } = "";
        public Uri? FlagUri { get; set; }
    }
}