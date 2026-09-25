using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OmniKiosk.Wpf.Controls;
using OmniKiosk.Wpf.Sdk.Printer;
using OmniKiosk.Wpf.Services;
using OmniKiosk.Wpf.Services.MoneyExchange;
using OmniKiosk.Wpf.Services.MoneyReceiver;

namespace OmniKiosk.Wpf.Views.MoneyExchange.Steps
{
    public partial class CashInStep : UserControl, IStepNav
    {
        private readonly MoneyExchangeFlowController _ctl;
        private readonly MoneyReceiverService _svc = GlobalHardwareManager.MoneyReceiver;
        private readonly BixolonPrinterService _printerSvc = GlobalHardwareManager.Printer;
        private readonly MoneyExchangeApiClient _api = new();

        public event EventHandler? NextRequested;
        public event EventHandler? BackRequested;
        public event EventHandler? ExitRequested;
        private bool _waitingForStackConfirmation;

        private double _totalForeign;
        private double _pendingEscrowValue;
        private decimal _targetForeign;
        private decimal _targetMyr;
        private int _maxMyrAvailable;
        private int _noteSequence;
        private bool _acceptanceBlocked;
        private bool _escrowValidationInProgress;
        private List<int> _acceptedDenominations = new();

        public CashInStep(MoneyExchangeFlowController ctl)
        {
            InitializeComponent();
            _ctl = ctl;
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            TitleText.Text = L10n.T("Mx_CashIn", "Insert Cash");
            SubtitleText.Text = L10n.T("Mx_CashInSubtitle", "Insert your foreign currency notes one at a time.");
            RateLabel.Text = L10n.T("Mx_RateLabel", "Rate");
            InsertedLabel.Text = L10n.T("Mx_TotalInserted", "SUCCESSFULLY ACCEPTED");
            EquivalentLabel.Text = L10n.T("Mx_EquivalentAmount", "EXACT CONVERSION");
            PayableLabel.Text = L10n.T("Mx_PayableAmount", "YOU WILL RECEIVE");
            SlotHintText.Text = L10n.T("Mx_SlotHint", "Insert one note at a time and wait for confirmation.");
            NoteDetectedLabel.Text = L10n.T("Mx_NoteDetected", "Note Detected");

            TxtEscrowDisclaimer.Text = L10n.T(
                "Mx_EscrowDisclaimer",
                "Once you choose Accept Note, this banknote will be stored inside the kiosk and CANNOT be returned. Please check the note amount carefully before continuing.");

            BtnEscrowReturn.Content = L10n.T("Mx_ReturnNote", "Return Note");
            BtnEscrowAccept.Content = L10n.T("Mx_AcceptNote", "Accept Note");
            BtnBack.Content = L10n.T("Mx_CancelTransaction", "Cancel Transaction");
            BtnNext.Content = L10n.T("Mx_FinishGetCash", "Finish Exchange");
            DoneTitle.Text = L10n.T("Mx_AcceptanceStopped", "Cash Accepted");
            DoneSubtitle.Text = L10n.T("Mx_ProceedingToDispense", "Preparing your MYR payout...");

            TxtLiveRate.Text = $"1 {_ctl.State.FromCurrency} = RM {_ctl.State.RateToMyr:0.00##}";

            _targetForeign = _ctl.State.IntendedFromAmount > 0
                ? (decimal)_ctl.State.IntendedFromAmount
                : (decimal)_ctl.State.FromAmount;

            _targetMyr = _ctl.State.IntendedMyrAmount > 0
                ? (decimal)_ctl.State.IntendedMyrAmount
                : MoneyExchangeAmountCalculator.GetPayableMyr(_targetForeign, (decimal)_ctl.State.RateToMyr);

            if (_targetForeign <= 0)
            {
                CustomDialog.ShowError("Invalid Exchange Amount", "The selected exchange amount is missing. Please start the transaction again.");
                ExitRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            TxtTargetForeign.Text = $"{_targetForeign:0.##} {_ctl.State.FromCurrency}";

            // CashIn starts with no physically accepted cash.
            _totalForeign = 0;
            _pendingEscrowValue = 0;
            _ctl.State.FromAmount = 0;
            _ctl.State.MyrAmount = 0;
            _ctl.State.CashInsertedMyr = 0;

            try
            {
                _maxMyrAvailable = GlobalHardwareManager.MoneyDispenser?.GetTotalAvailableMyr() ?? 0;
            }
            catch (Exception ex)
            {
                KioskLocalLogger.LogError("CashIn", "Unable to read MYR dispenser balance: " + ex.Message);
                _maxMyrAvailable = 0;
            }

            if (_maxMyrAvailable <= 0)
            {
                CustomDialog.ShowError(
                    L10n.T("Mx_OutOfCashTitle", "Out of Cash"),
                    L10n.T("Mx_OutOfCashBody", "Sorry, this kiosk is currently unable to dispense MYR cash. Please proceed to the counter."));

                BackRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (!await CheckInitialComplianceAsync())
                return;

            await LoadAcceptedDenominationsAsync();

            _svc.OnLog += Svc_OnLog;
            _svc.OnStatus += Svc_OnStatus;
            _svc.OnError += Svc_OnError;
            _svc.OnEscrow += Svc_OnEscrow;
            _svc.OnStacked += Svc_OnStacked;
            _svc.OnReturned += Svc_OnReturned;
            _svc.OnRejected += Svc_OnRejected;

            UpdateConversionUI();

            try { _svc.EnableAcceptance(true); }
            catch (Exception ex) { KioskLocalLogger.LogError("CashIn", "Failed to enable note acceptance: " + ex.Message); }

            TxtStatus.Text = "Machine ready. Insert your first note.";

            _ = _api.LogJourneyEventAsync(_ctl.State.SessionId, "MoneyExchange", "StepEntered", "CashIn");
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            try { _svc.EnableAcceptance(false); } catch { }

            _svc.OnLog -= Svc_OnLog;
            _svc.OnStatus -= Svc_OnStatus;
            _svc.OnError -= Svc_OnError;
            _svc.OnEscrow -= Svc_OnEscrow;
            _svc.OnStacked -= Svc_OnStacked;
            _svc.OnReturned -= Svc_OnReturned;
            _svc.OnRejected -= Svc_OnRejected;
        }

        private async Task LoadAcceptedDenominationsAsync()
        {
            try
            {
                var denoms = await _api.GetDenominationsAsync(_ctl.State.FromCurrency);
                _acceptedDenominations = denoms.Select(x => x.DenominationValue).OrderBy(x => x).ToList();

                AcceptedDenomsChipsPanel.Children.Clear();

                foreach (var value in _acceptedDenominations)
                {
                    var chip = new Border
                    {
                        Background = (Brush)Application.Current.Resources["BackgroundBrush"],
                        BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(18),
                        Padding = new Thickness(14, 7, 14, 7),
                        Margin = new Thickness(4)
                    };

                    chip.Child = new TextBlock
                    {
                        Text = value.ToString(),
                        FontSize = 15,
                        FontWeight = FontWeights.Bold,
                        Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"]
                    };

                    AcceptedDenomsChipsPanel.Children.Add(chip);
                }

                AcceptedDenomsLabel.Text = $"Accepted {_ctl.State.FromCurrency} notes";
                AcceptedDenomsPanel.Visibility = _acceptedDenominations.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                KioskLocalLogger.LogError("CashIn", "Failed to load accepted denominations: " + ex.Message);
                AcceptedDenomsPanel.Visibility = Visibility.Collapsed;
            }
        }

        private async Task<bool> CheckInitialComplianceAsync()
        {
            if (!_ctl.State.SenderId.HasValue)
            {
                CustomDialog.ShowError(
                    L10n.T("Mx_LimitExceededTitle", "Unable to Proceed at This Kiosk"),
                    L10n.T("Mx_LimitCheckFailedBody", "We couldn't verify transaction limits for this customer. Please proceed to the counter for assistance."));

                ExitRequested?.Invoke(this, EventArgs.Empty);
                return false;
            }

            try
            {
                var kioskId = await KioskAuthService.GetKioskIdAsync();
                var result = await _api.CheckLimitsAsync(_ctl.State.SenderId.Value, kioskId, 0);

                if (result.IsWithinLimits)
                    return true;

                CustomDialog.ShowError(
                    L10n.T("Mx_LimitExceededTitle", "Unable to Proceed at This Kiosk"),
                    GetLimitMessage(result.BreachedLimit));

                ExitRequested?.Invoke(this, EventArgs.Empty);
                return false;
            }
            catch (Exception ex)
            {
                KioskLocalLogger.LogError("CashIn", "Initial limit check failed: " + ex.Message);

                CustomDialog.ShowError(
                    L10n.T("Mx_LimitExceededTitle", "Unable to Proceed at This Kiosk"),
                    L10n.T("Mx_LimitCheckFailedBody", "We couldn't verify transaction limits for this customer. Please proceed to the counter for assistance."));

                ExitRequested?.Invoke(this, EventArgs.Empty);
                return false;
            }
        }

        private string GetLimitMessage(string? limit)
        {
            return limit switch
            {
                "PerTransaction" => L10n.T("Mx_LimitPerTxnBody", "This amount exceeds the maximum allowed for a single transaction."),
                "Daily" => L10n.T("Mx_LimitDailyBody", "This amount would exceed the customer's daily exchange limit."),
                "Rolling30Day" => L10n.T("Mx_LimitMonthlyBody", "This amount would exceed the customer's rolling 30-day exchange limit."),
                _ => L10n.T("Mx_LimitGenericBody", "This amount exceeds an applicable transaction limit.")
            };
        }

        private void Svc_OnLog(string message)
        {
            System.Diagnostics.Debug.WriteLine("[CashIn] " + message);
        }

        private void Svc_OnStatus(string status)
        {
            Dispatcher.Invoke(() =>
            {
                if (!_escrowValidationInProgress && EscrowOverlay.Visibility != Visibility.Visible)
                    TxtStatus.Text = status;
            });
        }

        private void Svc_OnError(string error)
        {
            KioskLocalLogger.LogError("CashIn", error);
        }

        private void Svc_OnRejected(string reason)
        {
            Dispatcher.Invoke(() =>
            {
                bool denomIssue = reason.Contains("Unrecognized note", StringComparison.OrdinalIgnoreCase)
                    || reason.Contains("currently disabled", StringComparison.OrdinalIgnoreCase);

                string partial = _totalForeign > 0
                    ? $"\n\n{_totalForeign:0.##} {_ctl.State.FromCurrency} is already accepted. You can try another note or finish with the accepted amount."
                    : "";

                if (denomIssue && _acceptedDenominations.Count > 0)
                {
                    CustomDialog.ShowWarning(
                        L10n.T("Mx_NoteNotAccepted", "Note Not Accepted"),
                        $"This kiosk does not accept that {_ctl.State.FromCurrency} note.\n\nAccepted notes: {string.Join(", ", _acceptedDenominations)}{partial}");
                }
                else
                {
                    CustomDialog.ShowWarning(
                        L10n.T("Mx_NoteRejectedTitle", "Note Rejected"),
                        L10n.T("Mx_NoteRejectedBody", "The machine could not accept that note. Please flatten the note and try again, or try a different note.")
                        + $"\n\nReason: {reason}{partial}");
                }

                RefreshFinishButton();
            });
        }

        private async void Svc_OnEscrow(EscrowInfo info)
        {
            if (_escrowValidationInProgress)
                return;

            _escrowValidationInProgress = true;

            try
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    TxtStatus.Text = "Checking note...";
                    BtnNext.IsEnabled = false;
                });

                if (info == null || info.Value <= 0)
                {
                    ReturnEscrowNote();

                    await Dispatcher.InvokeAsync(() =>
                        CustomDialog.ShowWarning("Invalid Item", "This kiosk only accepts valid currency banknotes."));

                    return;
                }

                string expectedCurrency = _ctl.State.FromCurrency;
                string insertedCurrency = string.IsNullOrWhiteSpace(info.CurrencyCode) ? "UNKNOWN" : info.CurrencyCode;

                bool currencyMatches = expectedCurrency.Length >= 2
                    && insertedCurrency.Length >= 2
                    && insertedCurrency.Substring(0, 2).Equals(expectedCurrency.Substring(0, 2), StringComparison.OrdinalIgnoreCase);

                if (!currencyMatches)
                {
                    ReturnEscrowNote();

                    await Dispatcher.InvokeAsync(() =>
                        CustomDialog.ShowWarning(
                            L10n.T("Mx_InvalidCurrencyTitle", "Invalid Currency"),
                            $"Please insert {expectedCurrency} notes only.\n\nDetected: {info.Value:0.##} {insertedCurrency}"));

                    return;
                }

                int denomination = Convert.ToInt32(info.Value);

                if (_acceptedDenominations.Count > 0 && !_acceptedDenominations.Contains(denomination))
                {
                    ReturnEscrowNote();

                    await Dispatcher.InvokeAsync(() =>
                        CustomDialog.ShowWarning(
                            L10n.T("Mx_NoteNotAccepted", "Note Not Accepted"),
                            $"The {info.Value:0.##} {expectedCurrency} note is not enabled for this kiosk.\n\nAccepted notes: {string.Join(", ", _acceptedDenominations)}"));

                    return;
                }

                decimal proposedForeign = (decimal)_totalForeign + (decimal)info.Value;

                // Target is flexible downward, but never accept more than
                // the customer explicitly selected.
                if (proposedForeign > _targetForeign)
                {
                    ReturnEscrowNote();

                    decimal remaining = Math.Max(0, _targetForeign - (decimal)_totalForeign);

                    await Dispatcher.InvokeAsync(() =>
                        CustomDialog.ShowWarning(
                            "Amount Exceeds Your Target",
                            $"Accepting this note would exceed your selected target of {_targetForeign:0.##} {expectedCurrency}.\n\n"
                            + $"Remaining to target: {remaining:0.##} {expectedCurrency}"
                            + (_totalForeign > 0
                                ? $"\n\nYou can try a smaller note or finish with {_totalForeign:0.##} {expectedCurrency}."
                                : "\n\nPlease insert a smaller note.")));

                    return;
                }

                decimal proposedMyr = MoneyExchangeAmountCalculator.GetPayableMyr(
                    proposedForeign,
                    (decimal)_ctl.State.RateToMyr);

                if (!_ctl.State.SenderId.HasValue)
                {
                    ReturnEscrowNote();
                    await HandleEscrowSystemFailureAsync("We couldn't verify the customer's transaction limits.");
                    return;
                }

                // Compliance validation BEFORE note becomes irreversible.
                try
                {
                    var kioskId = await KioskAuthService.GetKioskIdAsync();
                    var limit = await _api.CheckLimitsAsync(_ctl.State.SenderId.Value, kioskId, proposedMyr);

                    if (!limit.IsWithinLimits)
                    {
                        ReturnEscrowNote();

                        await Dispatcher.InvokeAsync(() =>
                        {
                            string partial = _totalForeign > 0
                                ? $"\n\n{_totalForeign:0.##} {_ctl.State.FromCurrency} has already been accepted. You may finish with that amount."
                                : "";

                            CustomDialog.ShowWarning("Transaction Limit", GetLimitMessage(limit.BreachedLimit) + partial);
                        });

                        return;
                    }
                }
                catch (Exception ex)
                {
                    KioskLocalLogger.LogError("CashIn", "Escrow limit validation failed: " + ex.Message);
                    ReturnEscrowNote();
                    await HandleEscrowSystemFailureAsync("We couldn't verify transaction limits for this note.");
                    return;
                }

                // Basic physical MYR balance.
                if (proposedMyr > _maxMyrAvailable)
                {
                    ReturnEscrowNote();

                    await Dispatcher.InvokeAsync(() =>
                    {
                        string partial = _totalForeign > 0
                            ? $"\n\nYou can finish with {_totalForeign:0.##} {_ctl.State.FromCurrency} already accepted."
                            : "";

                        CustomDialog.ShowWarning(
                            "Payout Not Available",
                            "The kiosk does not currently have enough MYR cash to support the payout after accepting this note." + partial);
                    });

                    return;
                }

                // Check actual cassette combination.
                try
                {
                    var kioskId = await KioskAuthService.GetKioskIdAsync();
                    var availability = await _api.GetDenominationBreakdownAsync(kioskId, proposedMyr);

                    if (!availability.CanDispenseFully)
                    {
                        ReturnEscrowNote();

                        await Dispatcher.InvokeAsync(() =>
                        {
                            string partial = _totalForeign > 0
                                ? $"\n\nYou can finish with {_totalForeign:0.##} {_ctl.State.FromCurrency} already accepted."
                                : "";

                            CustomDialog.ShowWarning(
                                "Payout Cannot Be Prepared",
                                "The kiosk cannot prepare the required MYR denomination combination after accepting this note." + partial);
                        });

                        return;
                    }
                }
                catch (Exception ex)
                {
                    KioskLocalLogger.LogError("CashIn", "Escrow payout availability check failed: " + ex.Message);
                    ReturnEscrowNote();
                    await HandleEscrowSystemFailureAsync("We couldn't confirm that the MYR payout can be prepared.");
                    return;
                }

                // All validation passed. The note is still returnable.
                _pendingEscrowValue = info.Value;

                await Dispatcher.InvokeAsync(() =>
                {
                    TxtEscrowAmount.Text = $"{info.Value:0.##} {expectedCurrency}";
                    TxtEscrowCurrentAccepted.Text = $"{_totalForeign:0.##} {expectedCurrency}";
                    TxtEscrowAfterAccept.Text = $"{proposedForeign:0.##} {expectedCurrency}";
                    TxtEscrowPayout.Text = $"RM {proposedMyr:0}";
                    BtnEscrowAccept.Content = $"Accept {info.Value:0.##} {expectedCurrency}";
                    BtnEscrowAccept.IsEnabled = true;
                    EscrowOverlay.Visibility = Visibility.Visible;
                });
            }
            catch (Exception ex)
            {
                KioskLocalLogger.LogError("CashIn", "Unexpected escrow handling error: " + ex.Message);
                ReturnEscrowNote();
                await HandleEscrowSystemFailureAsync("The kiosk could not safely process this note.");
            }
            finally
            {
                _escrowValidationInProgress = false;

                await Dispatcher.InvokeAsync(() =>
                {
                    if (EscrowOverlay.Visibility != Visibility.Visible)
                    {
                        TxtStatus.Text = _totalForeign > 0
                            ? "Insert another note or finish with the accepted amount."
                            : "Machine ready. Insert your note.";
                    }

                    RefreshFinishButton();
                });
            }
        }

        private async Task HandleEscrowSystemFailureAsync(string message)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                if (_totalForeign <= 0)
                {
                    try { _svc.EnableAcceptance(false); } catch { }

                    CustomDialog.ShowError(
                        "Unable to Continue",
                        message + "\n\nNo cash has been accepted. Please proceed to the counter.");

                    ExitRequested?.Invoke(this, EventArgs.Empty);
                    return;
                }

                // Once cash is in the vault, do not abandon the customer.
                _acceptanceBlocked = true;

                try { _svc.EnableAcceptance(false); } catch { }

                TxtStatus.Text = "Please finish with the amount already accepted.";

                CustomDialog.ShowWarning(
                    "Finish Current Exchange",
                    message
                    + $"\n\n{_totalForeign:0.##} {_ctl.State.FromCurrency} has already been accepted and cannot be returned by the kiosk."
                    + "\n\nPlease finish the exchange with the accepted amount.");

                RefreshFinishButton();
            });
        }

        private void ReturnEscrowNote()
        {
            _pendingEscrowValue = 0;

            try { _svc.EscrowReturn(); }
            catch (Exception ex) { KioskLocalLogger.LogError("CashIn", "Failed to return escrow note: " + ex.Message); }
        }
        private void Svc_OnStacked(EscrowInfo info)
        {
            Dispatcher.Invoke(() =>
            {
                // Some receiver states can produce more than one stacked callback
                // for the same physical note. Only the callback corresponding to
                // our current EscrowStack request is allowed to change totals.
                if (!_waitingForStackConfirmation)
                {
                    KioskLocalLogger.LogInfo(
                        "CashIn",
                        "Duplicate stacked callback ignored.");

                    return;
                }

                if (_pendingEscrowValue <= 0)
                {
                    _waitingForStackConfirmation = false;

                    KioskLocalLogger.LogError(
                        "CashIn",
                        "Stack confirmation received but pending escrow amount was empty.");

                    return;
                }

                double acceptedValue = _pendingEscrowValue;

                _waitingForStackConfirmation = false;
                _pendingEscrowValue = 0;

                _totalForeign += acceptedValue;

                _ = SaveNoteAcceptedAsync(acceptedValue);

                UpdateConversionUI();

                if ((decimal)_totalForeign >= _targetForeign)
                {
                    _acceptanceBlocked = true;

                    try { _svc.EnableAcceptance(false); }
                    catch { }

                    TxtStatus.Text =
                        "Target reached. Please finish the exchange.";
                }
                else
                {
                    TxtStatus.Text =
                        "Note accepted. Insert another note or finish with the accepted amount.";
                }

                RefreshFinishButton();
            });
        }
        //private void Svc_OnStacked(EscrowInfo info)
        //{
        //    Dispatcher.Invoke(() =>
        //    {
        //        double acceptedValue = _pendingEscrowValue;

        //        if (acceptedValue <= 0)
        //        {
        //            KioskLocalLogger.LogError("CashIn", "Stacked event received without a pending escrow value.");
        //            return;
        //        }

        //        _totalForeign += acceptedValue;
        //        _pendingEscrowValue = 0;

        //        _ = SaveNoteAcceptedAsync(acceptedValue);

        //        UpdateConversionUI();

        //        if ((decimal)_totalForeign >= _targetForeign)
        //        {
        //            _acceptanceBlocked = true;

        //            try { _svc.EnableAcceptance(false); } catch { }

        //            TxtStatus.Text = "Target reached. Please finish the exchange.";
        //        }
        //        else
        //        {
        //            TxtStatus.Text = "Note accepted. Insert another note or finish with the accepted amount.";
        //        }

        //        RefreshFinishButton();
        //    });
        //}

        private async Task SaveNoteAcceptedAsync(double acceptedValue)
        {
            try
            {
                if (_ctl.State.TransactionId == null)
                {
                    var kioskUserId = await KioskAuthService.GetKioskUserIdAsync();

                    var result = await _api.CreateTransactionAsync(new CreateTransactionApiRequest
                    {
                        KioskId = await KioskAuthService.GetKioskIdAsync(),
                        BranchId = await KioskAuthService.GetKioskBranchIdAsync(),
                        CustomerRef = _ctl.State.SenderId,
                        ScreeningTransGuid = _ctl.State.ScreeningTransGuid,
                        FromCurrency = _ctl.State.FromCurrency,
                        FromAmount = 0,
                        Rate = (decimal)_ctl.State.RateToMyr,
                        MyrAmount = 0,
                        CashInsertedMyr = 0,
                        CreatedBy = kioskUserId.ToString()
                    });

                    _ctl.State.TransactionId = result.TransactionId;
                    _ctl.State.ReceiptNo = result.ReceiptNo;
                }

                int sequence = Interlocked.Increment(ref _noteSequence);

                _ctl.State.LastTransactionNoteSequence = _noteSequence;

                await _api.RecordNoteAsync(
                    _ctl.State.TransactionId.Value,
                    _noteSequence,
                    _ctl.State.FromCurrency,
                    (decimal)acceptedValue,
                    "Accepted");
            }
            catch (Exception ex)
            {
                // Cash has already physically moved. Never interrupt customer.
                KioskLocalLogger.LogError("CashIn", "Failed to persist accepted note: " + ex.Message);
            }
        }

        private void Svc_OnReturned(EscrowInfo info)
        {
            Dispatcher.Invoke(() =>
            {
                KioskLocalLogger.LogInfo("CashIn", "Hardware confirmed escrow return.");
                RefreshFinishButton();
            });
        }

        private async Task SaveNoteReturnedAsync(double returnedValue)
        {
            if (!_ctl.State.TransactionId.HasValue)
                return;

            try
            {
                int sequence = Interlocked.Increment(ref _noteSequence);
                _ctl.State.LastTransactionNoteSequence = _noteSequence;

                await _api.RecordNoteAsync(
                    _ctl.State.TransactionId.Value,
                    sequence,
                    _ctl.State.FromCurrency,
                    (decimal)returnedValue,
                    "Returned");
            }
            catch (Exception ex)
            {
                KioskLocalLogger.LogError("CashIn", "Failed to persist returned note: " + ex.Message);
            }
        }
        private void EscrowAccept_Click(object sender, RoutedEventArgs e)
        {
            if (_pendingEscrowValue <= 0)
                return;

            BtnEscrowAccept.IsEnabled = false;
            EscrowOverlay.Visibility = Visibility.Collapsed;
            _waitingForStackConfirmation = true;

            try
            {
                _svc.EscrowStack();
            }
            catch (Exception ex)
            {
                _waitingForStackConfirmation = false;
                BtnEscrowAccept.IsEnabled = true;

                KioskLocalLogger.LogError(
                    "CashIn",
                    "EscrowStack failed: " + ex.Message);

                ReturnEscrowNote();

                CustomDialog.ShowError(
                    "Unable to Accept Note",
                    "The machine could not store this note. The note will be returned.");
            }
        }
        //private void EscrowAccept_Click(object sender, RoutedEventArgs e)
        //{
        //    if (_pendingEscrowValue <= 0)
        //        return;

        //    BtnEscrowAccept.IsEnabled = false;
        //    EscrowOverlay.Visibility = Visibility.Collapsed;

        //    try
        //    {
        //        _svc.EscrowStack();
        //    }
        //    catch (Exception ex)
        //    {
        //        KioskLocalLogger.LogError("CashIn", "EscrowStack failed: " + ex.Message);
        //        BtnEscrowAccept.IsEnabled = true;
        //        ReturnEscrowNote();

        //        CustomDialog.ShowError(
        //            "Unable to Accept Note",
        //            "The machine could not store this note. The note will be returned.");
        //    }
        //}

        private void EscrowReturn_Click(object sender, RoutedEventArgs e)
        {
            double returnedValue = _pendingEscrowValue;

            _pendingEscrowValue = 0;
            EscrowOverlay.Visibility = Visibility.Collapsed;
            BtnEscrowAccept.IsEnabled = true;
            _waitingForStackConfirmation = false;
            try { _svc.EscrowReturn(); }
            catch (Exception ex) { KioskLocalLogger.LogError("CashIn", "EscrowReturn failed: " + ex.Message); }

            // The escrow note was never added to _totalForeign.
            if (returnedValue > 0)
                _ = SaveNoteReturnedAsync(returnedValue);

            RefreshFinishButton();
        }

        private void UpdateConversionUI()
        {
            if (_totalForeign < 0)
                _totalForeign = 0;

            decimal actualForeign = (decimal)_totalForeign;
            decimal exactMyr = MoneyExchangeAmountCalculator.GetExactMyr(actualForeign, (decimal)_ctl.State.RateToMyr);
            decimal payableMyr = MoneyExchangeAmountCalculator.GetPayableMyr(actualForeign, (decimal)_ctl.State.RateToMyr);
            decimal remaining = Math.Max(0, _targetForeign - actualForeign);

            TxtInsertedForeign.Text = $"{actualForeign:0.##} {_ctl.State.FromCurrency}";
            TxtRemainingForeign.Text = remaining > 0
                ? $"Remaining to target: {remaining:0.##} {_ctl.State.FromCurrency}"
                : "Target reached";

            TxtEquivalentMyr.Text = $"RM {exactMyr:0.00}";
            TxtPayableMyr.Text = $"RM {payableMyr:0}";

            decimal progress = _targetForeign > 0
                ? Math.Min(100m, actualForeign / _targetForeign * 100m)
                : 0;

            TargetProgressBar.Value = (double)progress;
            TxtProgressCaption.Text = $"{progress:0}% of target";

            // FromAmount/MyrAmount now mean real physically accepted cash.
            _ctl.State.FromAmount = (double)actualForeign;
            _ctl.State.MyrAmount = (double)payableMyr;

            RefreshFinishButton();
        }

        private void RefreshFinishButton()
        {
            bool canFinish = _totalForeign > 0
                && _pendingEscrowValue <= 0
                && EscrowOverlay.Visibility != Visibility.Visible;

            BtnNext.IsEnabled = canFinish;

            BtnNext.Content = canFinish
                ? $"Finish with {_totalForeign:0.##} {_ctl.State.FromCurrency}  →  RM {_ctl.State.MyrAmount:0}"
                : L10n.T("Mx_FinishGetCash", "Finish Exchange");
        }

        private async void Back_Click(object sender, RoutedEventArgs e)
        {
            if (_pendingEscrowValue > 0 || EscrowOverlay.Visibility == Visibility.Visible)
            {
                CustomDialog.ShowWarning(
                    "Note Pending",
                    "Please choose Accept Note or Return Note before leaving this screen.");

                return;
            }

            if (_totalForeign <= 0)
            {
                try { _svc.EnableAcceptance(false); } catch { }
                BackRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            bool confirmed = CustomDialog.ShowQuestion(
                L10n.T("Mx_CancelConfirmTitle", "Cancel this transaction?"),
                string.Format(
                    L10n.T("Mx_CancelConfirmBody", "You've already inserted {0:0.00} {1} ({2}). If you cancel now, we'll print a slip so you can collect this amount at the counter."),
                    _totalForeign,
                    _ctl.State.FromCurrency,
                    TxtEquivalentMyr.Text),
                L10n.T("Mx_CancelConfirmYes", "Yes, Cancel"),
                L10n.T("Mx_CancelConfirmNo", "No, Continue"));

            if (!confirmed)
                return;

            try { _svc.EnableAcceptance(false); } catch { }

            PrintCancelSlip();

            if (_ctl.State.TransactionId.HasValue)
            {
                try
                {
                    await _api.CompleteTransactionAsync(_ctl.State.TransactionId.Value, "Cancelled");
                }
                catch (Exception ex)
                {
                    KioskLocalLogger.LogError("CashIn", "Failed to mark transaction cancelled: " + ex.Message);
                }
            }

            _ = _api.LogJourneyEventAsync(
                _ctl.State.SessionId,
                "MoneyExchange",
                "StepAbandoned",
                "CashIn",
                outcome: "Failure",
                details: $"Cancelled after {_totalForeign:0.##} {_ctl.State.FromCurrency} was accepted",
                transactionId: _ctl.State.TransactionId);

            await Task.Delay(300);
            ExitRequested?.Invoke(this, EventArgs.Empty);
        }

        private void PrintCancelSlip()
        {
            try
            {
                var s = _ctl.State;
                var customerName = s.Customer?.FullName ?? "Walk-in Customer";
                var maskedDocument = ReceiptFormatter.MaskDocumentNo(s.Customer?.IdNo);

                var receiptNo = !string.IsNullOrWhiteSpace(s.ReceiptNo)
                    ? s.ReceiptNo
                    : ReceiptFormatter.BuildReceiptNo(s.TransactionId, KioskAuthService.CachedKioskIdOrNull ?? "UNKNOWN");

                var receipt = new StringBuilder();

                receipt.Append(ReceiptFormatter.BuildHeader("CASH"));
                receipt.Append(BixolonPrinterService.ESC_BOLD_ON);
                receipt.Append("*** COUNTER COLLECTION SLIP ***\n");
                receipt.Append(BixolonPrinterService.ESC_BOLD_OFF);
                receipt.Append(ReceiptFormatter.BuildCustomerBlock(receiptNo, customerName, maskedDocument));
                receipt.Append(BixolonPrinterService.ESC_ALIGN_LEFT);
                receipt.Append("Status: TRANSACTION CANCELLED BY CUSTOMER\n");
                receipt.Append($"Inserted: {_totalForeign:0.##} {s.FromCurrency}\n");
                receipt.Append("--------------------------------\n");
                receipt.Append(BixolonPrinterService.ESC_ALIGN_CENTER);
                receipt.Append(BixolonPrinterService.ESC_BOLD_ON);
                receipt.Append("AMOUNT OWED TO CUSTOMER\n");
                receipt.Append(BixolonPrinterService.ESC_DOUBLE_SIZE);
                receipt.Append($"RM {s.MyrAmount:0}\n");
                receipt.Append(BixolonPrinterService.ESC_NORMAL_SIZE);
                receipt.Append(BixolonPrinterService.ESC_BOLD_OFF);
                receipt.Append(ReceiptFormatter.BuildFooter(success: false));

                _printerSvc?.PrintReceipt(receipt.ToString());
            }
            catch (Exception ex)
            {
                CustomDialog.ShowError(L10n.T("Mx_PrintErrorTitle", "Print Error"), ex.Message);
            }
        }

        private async void Next_Click(object sender, RoutedEventArgs e)
        {
            if (!BtnNext.IsEnabled || _totalForeign <= 0 || _pendingEscrowValue > 0)
                return;

            try { _svc.EnableAcceptance(false); } catch { }

            BtnNext.IsEnabled = false;
            DoneOverlay.Visibility = Visibility.Visible;

            _ctl.State.CashInsertedMyr = _ctl.State.MyrAmount;

            if (_ctl.State.TransactionId.HasValue)
            {
                try
                {
                    await _api.UpdateTransactionAmountsAsync(
                        _ctl.State.TransactionId.Value,
                        (decimal)_ctl.State.FromAmount,
                        (decimal)_ctl.State.MyrAmount,
                        (decimal)_ctl.State.CashInsertedMyr);
                }
                catch (Exception ex)
                {
                    KioskLocalLogger.LogError(
                        "CashIn",
                        $"Failed to update transaction {_ctl.State.TransactionId}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            _ = _api.LogJourneyEventAsync(
                _ctl.State.SessionId,
                "MoneyExchange",
                "StepCompleted",
                "CashIn",
                outcome: "Success",
                details: $"Target {_targetForeign:0.##} {_ctl.State.FromCurrency}; actual {_ctl.State.FromAmount:0.##} {_ctl.State.FromCurrency}; payout RM {_ctl.State.MyrAmount:0}",
                transactionId: _ctl.State.TransactionId);

            await Task.Delay(1000);
            NextRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}