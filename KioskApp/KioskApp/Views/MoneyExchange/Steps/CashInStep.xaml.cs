using System;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OmniKiosk.Wpf.Controls;
using OmniKiosk.Wpf.Sdk.Printer;
using OmniKiosk.Wpf.Services.MoneyExchange;
using OmniKiosk.Wpf.Services.MoneyReceiver;
using OmniKiosk.Wpf.Services;

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

        private double _totalForeign = 0;
        private double _pendingEscrowValue = 0;
        private List<int> _acceptedDenominations = new();
        private int _maxMyrAvailable = 0;
        private int _noteSequence = 0;

        public CashInStep(MoneyExchangeFlowController ctl)
        {
            InitializeComponent();
            _ctl = ctl;
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            TitleText.Text = L10n.T("Mx_CashIn", "Insert Cash");
            SubtitleText.Text = L10n.T("Mx_CashInSubtitle", "Please insert your notes into the acceptor below.");
            RateLabel.Text = L10n.T("Mx_RateLabel", "Rate:");
            InsertedLabel.Text = L10n.T("Mx_TotalInserted", "TOTAL INSERTED (FOREIGN)");
            EquivalentLabel.Text = L10n.T("Mx_EquivalentAmount", "EQUIVALENT AMOUNT (MYR)");
            PayableLabel.Text = L10n.T("Mx_PayableAmount", "PAYABLE AMOUNT (ROUNDED)");
            TxtStatus.Text = L10n.T("Mx_MachineReady", "Machine is ready and accepting notes…");
            SlotHintText.Text = L10n.T("Mx_SlotHint", "Insert one note at a time. Wait for confirmation before inserting the next.");
            NoteDetectedLabel.Text = L10n.T("Mx_NoteDetected", "Note Detected");
            TxtEscrowDisclaimer.Text = L10n.T("Mx_EscrowDisclaimer",
                "IMPORTANT: Once accepted, this note cannot be returned. Please check the amount carefully before choosing Accept or Return Note.");
            BtnEscrowReturn.Content = L10n.T("Mx_ReturnNote", "Return Note");
            BtnEscrowAccept.Content = L10n.T("Mx_AcceptNote", "Accept Note");
            DoneTitle.Text = L10n.T("Mx_AcceptanceStopped", "Acceptance Stopped");
            DoneSubtitle.Text = L10n.T("Mx_ProceedingToDispense", "Proceeding to dispense your cash…");
            BtnBack.Content = L10n.T("Mx_CancelTransaction", "Cancel Transaction");
            BtnNext.Content = L10n.T("Mx_FinishGetCash", "Finish & Get Cash ➔");

            TxtLiveRate.Text = $"1 {_ctl.State.FromCurrency} = {_ctl.State.RateToMyr:0.00} MYR";
            TxtInsertedForeign.Text = $"0.00 {_ctl.State.FromCurrency}";

            try
            {
                _maxMyrAvailable = GlobalHardwareManager.MoneyDispenser?.GetTotalAvailableMyr() ?? 5000;
            }
            catch { _maxMyrAvailable = 0; }

            if (_maxMyrAvailable <= 0)
            {
                CustomDialog.ShowError(
                    L10n.T("Mx_OutOfCashTitle", "Out of Cash"),
                    L10n.T("Mx_OutOfCashBody", "Sorry, this kiosk is currently out of MYR cash. Please proceed to the counter."));
                Task.Delay(1500).ContinueWith(_ => Dispatcher.Invoke(() => BackRequested?.Invoke(this, EventArgs.Empty)));
                return;
            }

            // Upfront compliance-limit gate, BEFORE any cash is accepted -
            // checks whether this customer has ALREADY exhausted their
            // daily/monthly limit from prior transactions today, using
            // ProposedMyrAmount=0 (i.e. "is there any room at all left").
            // Better to tell them now than let them insert cash only to be
            // cut off partway through - see Svc_OnStacked for the second,
            // incremental check as cash actually comes in.
            if (!await CheckComplianceLimitsAsync(0))
                return; // hard-stop dialog already shown and ExitRequested fired inside the helper

            // Accepted-denominations display, from Ksk_BanknoteDenominations
            // (KSK_GetDenominations) - the real, existing table for this,
            // not something new. Same source that already drives note
            // display/dispensing logic elsewhere. Shown as a row of small
            // chips rather than a sentence - reads at a glance, and the
            // panel is hidden entirely when the fetch returns nothing,
            // rather than showing an empty label with no chips under it.
            var denoms = await _api.GetDenominationsAsync(_ctl.State.FromCurrency);
            _acceptedDenominations = denoms.Select(d => d.DenominationValue).ToList();

            AcceptedDenomsChipsPanel.Children.Clear();
            if (_acceptedDenominations.Count > 0)
            {
                AcceptedDenomsLabel.Text = string.Format(L10n.T("Mx_AcceptedDenomsLabel", "Accepted {0} notes"), _ctl.State.FromCurrency);
                foreach (var value in _acceptedDenominations)
                {
                    var chip = new Border
                    {
                        Background = (Brush)Application.Current.Resources["BackgroundBrush"],
                        BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(20),
                        Padding = new Thickness(16, 8, 16, 8),
                        Margin = new Thickness(4)
                    };
                    chip.Child = new TextBlock
                    {
                        Text = value.ToString(),
                        FontSize = 16,
                        FontWeight = FontWeights.Bold,
                        Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"]
                    };
                    AcceptedDenomsChipsPanel.Children.Add(chip);
                }
                AcceptedDenomsPanel.Visibility = Visibility.Visible;
            }
            else
            {
                AcceptedDenomsPanel.Visibility = Visibility.Collapsed;
            }

            _svc.OnLog += Svc_OnLog;
            _svc.OnStatus += Svc_OnStatus;
            _svc.OnError += Svc_OnError;
            _svc.OnEscrow += Svc_OnEscrow;
            _svc.OnStacked += Svc_OnStacked;
            _svc.OnReturned += Svc_OnReturned;
            _svc.OnRejected += Svc_OnRejected;

            try { _svc.EnableAcceptance(true); } catch { }

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

        // Compliance limit check - KSK_CheckMoneyExchangeLimits via
        // MoneyExchangeApiClient.CheckLimitsAsync. Returns true if the
        // transaction may proceed. On a genuine breach, or if the check
        // itself couldn't be completed at all, shows the hard-stop dialog
        // and fires ExitRequested (not BackRequested - this follows the
        // CUSTOMER, not the kiosk, so retrying elsewhere wouldn't help,
        // unlike the "out of cash" case above which is kiosk-specific).
        private async Task<bool> CheckComplianceLimitsAsync(decimal proposedMyrAmount)
        {
            if (_ctl.State.SenderId == null)
            {
                // Shouldn't happen by the time CashInStep loads - SenderId
                // is resolved in CustomerDetailsStep before this step is
                // ever reached. Treated the same as an unreachable check:
                // cannot verify, so does not proceed. Logged loudly since
                // this points at a real bug elsewhere if it ever fires.
                KioskLocalLogger.LogError("CashIn", "CRITICAL: CheckComplianceLimitsAsync called with no SenderId set - blocking as a precaution.");
                ShowLimitHardStop(L10n.T("Mx_LimitCheckFailedBody", "We couldn't verify transaction limits for this customer. Please proceed to the counter for assistance."));
                return false;
            }

            try
            {
                var kioskId = await KioskAuthService.GetKioskIdAsync();
                var result = await _api.CheckLimitsAsync(_ctl.State.SenderId.Value, kioskId, proposedMyrAmount);

                if (result.IsWithinLimits) return true;

                string reason = result.BreachedLimit switch
                {
                    "PerTransaction" => L10n.T("Mx_LimitPerTxnBody", "This amount exceeds the maximum allowed for a single transaction. Please visit your nearest branch to complete this exchange."),
                    "Daily" => L10n.T("Mx_LimitDailyBody", "You have reached your daily exchange limit. Please visit your nearest branch, or try again tomorrow."),
                    "Rolling30Day" => L10n.T("Mx_LimitMonthlyBody", "You have reached your monthly exchange limit. Please visit your nearest branch to continue."),
                    _ => L10n.T("Mx_LimitGenericBody", "This transaction exceeds an applicable limit. Please visit your nearest branch to complete this exchange.")
                };
                ShowLimitHardStop(reason);
                return false;
            }
            catch (Exception ex)
            {
                // Unreachable/failed compliance check - fail CLOSED, the
                // opposite direction from the "out of cash" hardware check
                // above. See CheckLimitsAsync's own remarks for why: this
                // protects a regulatory control, not just customer
                // convenience, so an unverifiable check must not silently
                // let the transaction through.
                KioskLocalLogger.LogError("CashIn", "Compliance limit check failed (blocking as a precaution): " + ex.Message);
                ShowLimitHardStop(L10n.T("Mx_LimitCheckFailedBody", "We couldn't verify transaction limits for this customer. Please proceed to the counter for assistance."));
                return false;
            }
        }

        private void ShowLimitHardStop(string body)
        {
            try { _svc.EnableAcceptance(false); } catch { }
            CustomDialog.ShowError(L10n.T("Mx_LimitExceededTitle", "Unable to Proceed at This Kiosk"), body);
            ExitRequested?.Invoke(this, EventArgs.Empty);
        }

        private void Svc_OnLog(string s) => System.Diagnostics.Debug.WriteLine("[CashIn] " + s);
        private void Svc_OnStatus(string s) => Dispatcher.Invoke(() => TxtStatus.Text = s);
        private void Svc_OnError(string s) => KioskLocalLogger.LogError("CashIn", s);

        private void Svc_OnRejected(string reason) => Dispatcher.Invoke(() =>
        {
            // "Unrecognized note" and "Note type is currently disabled" are
            // the two hardware reasons (MoneyReceiverService's reject-code
            // table) that specifically mean "this denomination isn't one
            // we take here", as opposed to a torn/skewed/doubled note
            // (which is a physical handling problem, not a denomination
            // problem). Only these two get the accepted-denominations
            // message; everything else keeps the generic retry message.
            bool isDenominationIssue = reason.Contains("Unrecognized note", StringComparison.OrdinalIgnoreCase)
                || reason.Contains("currently disabled", StringComparison.OrdinalIgnoreCase);

            if (isDenominationIssue && _acceptedDenominations.Count > 0)
            {
                CustomDialog.ShowWarning(
                    L10n.T("Mx_NoteNotAccepted", "Note Not Accepted"),
                    string.Format(
                        L10n.T("Mx_NoteNotAcceptedBody", "This kiosk does not accept that {0} note.\n\nAccepted {0} notes: {1}"),
                        _ctl.State.FromCurrency, string.Join(", ", _acceptedDenominations)));
            }
            else
            {
                CustomDialog.ShowWarning(
                    L10n.T("Mx_NoteRejectedTitle", "Note Rejected"),
                    L10n.T("Mx_NoteRejectedBody", "The machine could not accept that note. Please flatten the note and try again, or try a different note.") + $" ({reason})");
            }
        });

        private void Svc_OnEscrow(EscrowInfo info) => Dispatcher.Invoke(() =>
        {
            if (info == null || info.Value <= 0)
            {
                try { _svc.EscrowReturn(); } catch { }
                CustomDialog.ShowInfo(
                    L10n.T("Mx_InvalidDocTitle", "Invalid Document"),
                    L10n.T("Mx_InvalidDocBody", "This machine only accepts valid currency notes. Barcodes, coupons, or unrecognized items are not accepted."));
                return;
            }

            string expectedCurrency = _ctl.State.FromCurrency;
            string insertedCurrency = string.IsNullOrWhiteSpace(info.CurrencyCode) ? "UNKNOWN" : info.CurrencyCode;

            bool isMatch = false;
            if (expectedCurrency.Length >= 2 && insertedCurrency.Length >= 2)
            {
                isMatch = insertedCurrency.Substring(0, 2).Equals(expectedCurrency.Substring(0, 2), StringComparison.OrdinalIgnoreCase);
            }

            if (!isMatch)
            {
                try { _svc.EscrowReturn(); } catch { }
                CustomDialog.ShowWarning(
                    L10n.T("Mx_InvalidCurrencyTitle", "Invalid Currency"),
                    string.Format(L10n.T("Mx_InvalidCurrencyBody", "Please insert {0} notes only. You inserted a {1} {2} note."), expectedCurrency, info.Value, insertedCurrency));
                return;
            }

            _pendingEscrowValue = info.Value;
            _totalForeign += _pendingEscrowValue;
            UpdateConversionUI();

            TxtEscrowAmount.Text = $"{info.Value} {expectedCurrency}";
            EscrowOverlay.Visibility = Visibility.Visible;
        });

        private void Svc_OnStacked(EscrowInfo info) => Dispatcher.Invoke(() =>
        {
            double acceptedValue = _pendingEscrowValue;
            _pendingEscrowValue = 0;
            BtnNext.IsEnabled = true;

            // The note is physically in the vault the instant this event
            // fires - everything below is best-effort record-keeping and
            // must never be allowed to affect what already happened in
            // hardware. Fire-and-forget with its own error handling.
            _ = SaveNoteAcceptedAsync(acceptedValue);

            if (_ctl.State.MyrAmount >= _maxMyrAvailable)
            {
                try { _svc.EnableAcceptance(false); } catch { }
                CustomDialog.ShowInfo(
                    L10n.T("Mx_LimitReachedTitle", "Limit Reached"),
                    string.Format(L10n.T("Mx_LimitReachedBody", "The machine only has RM {0} available. You cannot insert more notes. Please finish."), _maxMyrAvailable));
            }

            // Incremental compliance check, using the CURRENT accumulated
            // total - the note is already physically accepted by this
            // point (same as the hardware-availability check above), so
            // this can only stop FURTHER notes, not the one that just
            // pushed the total over. Fire-and-forget like SaveNoteAcceptedAsync
            // above - the result (disable acceptance + hard-stop dialog)
            // arrives a moment later, which is an acceptable trade-off
            // given the note is already committed either way.
            _ = CheckComplianceLimitsAsync((decimal)_ctl.State.MyrAmount);
        });

        private async Task SaveNoteAcceptedAsync(double acceptedValue)
        {
            try
            {
                // First accepted note - create the parent transaction record
                // before logging the note itself, so every note logged from
                // here on has a real TransactionId to attach to. Stored on
                // shared flow state so FinalReceiptStep can complete the
                // same record later.
                if (_ctl.State.TransactionId == null)
                {
                    var kioskUserId = await KioskAuthService.GetKioskUserIdAsync();
                    var (newId, generatedReceiptNo) = await _api.CreateTransactionAsync(new CreateTransactionApiRequest
                    {
                        KioskId = await KioskAuthService.GetKioskIdAsync(),
                        BranchId = await KioskAuthService.GetKioskBranchIdAsync(),
                        CustomerRef = _ctl.State.SenderId, // now resolved by CustomerDetailsStep's SenderMaster check
                        ScreeningTransGuid = _ctl.State.ScreeningTransGuid, // generated at flow start, committed at completion
                        FromCurrency = _ctl.State.FromCurrency,
                        FromAmount = 0, // running totals updated as notes come in - see below
                        Rate = (decimal)_ctl.State.RateToMyr,
                        MyrAmount = 0,
                        CashInsertedMyr = 0,
                        // Real numeric UserID from the kiosk's own login, not a made-up
                        // string. KSK_MirrorToMcTransaction later needs this to cast
                        // cleanly to int for Mc_TransMaster/Mc_Transaction - "KIOSK" as
                        // a string silently failed that cast and the whole mirror never
                        // ran, even though the customer-facing flow completed normally.
                        CreatedBy = kioskUserId.ToString()
                    });
                    _ctl.State.TransactionId = newId;
                    // Real sequential receipt number from KSK_GetReceiptNo, not the old
                    // client-generated ReceiptFormatter.BuildReceiptNo value.
                    _ctl.State.ReceiptNo = generatedReceiptNo;
                }

                _noteSequence++;
                await _api.RecordNoteAsync(_ctl.State.TransactionId.Value, _noteSequence, _ctl.State.FromCurrency, (decimal)acceptedValue, "Accepted");
            }
            catch (Exception ex)
            {
                // Deliberately not shown to the customer - the physical note
                // was already accepted and is sitting in the vault. A logging
                // failure here is a backend problem to investigate, not
                // something that should interrupt someone mid-transaction.
                KioskLocalLogger.LogError("CashIn", "Failed to save accepted note to API: " + ex.Message);
            }
        }

        private void Svc_OnReturned(EscrowInfo info) => Dispatcher.Invoke(() =>
        {
            // The running total and database record are now handled
            // synchronously in EscrowReturn_Click below, at the moment the
            // customer's decision is made - not here. This handler used to
            // be the ONLY place _totalForeign got decremented and the
            // "Returned" note got logged, but it depends on a hardware
            // confirmation event that isn't guaranteed to fire. If it
            // didn't, _totalForeign stayed permanently inflated by a note
            // that had already left the machine, and _pendingEscrowValue
            // would then be silently overwritten by the next note's escrow
            // event - losing the return record entirely. This is now a
            // pure confirmation/logging signal and must not touch
            // _totalForeign or _pendingEscrowValue again.
            KioskLocalLogger.LogInfo("CashIn", "Hardware confirmed note return.");
        });

        private async Task SaveNoteReturnedAsync(long transactionId, int sequenceNo, double returnedValue)
        {
            try
            {
                await _api.RecordNoteAsync(transactionId, sequenceNo, _ctl.State.FromCurrency, (decimal)returnedValue, "Returned");
            }
            catch (Exception ex)
            {
                KioskLocalLogger.LogError("CashIn", "Failed to save returned note to API: " + ex.Message);
            }
        }

        private void EscrowAccept_Click(object sender, RoutedEventArgs e)
        {
            EscrowOverlay.Visibility = Visibility.Collapsed;
            try { _svc.EscrowStack(); } catch { }
        }

        private void EscrowReturn_Click(object sender, RoutedEventArgs e)
        {
            EscrowOverlay.Visibility = Visibility.Collapsed;

            // Decrement the running total and log the returned note HERE,
            // synchronously with the customer's decision - not waiting for
            // the hardware's own asynchronous return-confirmation event
            // (Svc_OnReturned), which is not guaranteed to fire. See that
            // handler's comment for the bug this fixes.
            if (_pendingEscrowValue > 0)
            {
                double returnedValue = _pendingEscrowValue;
                _totalForeign -= returnedValue;
                if (_totalForeign < 0) _totalForeign = 0;

                _pendingEscrowValue = 0;
                UpdateConversionUI();
                BtnNext.IsEnabled = _totalForeign > 0;

                if (_ctl.State.TransactionId.HasValue)
                {
                    _noteSequence++;
                    var txnId = _ctl.State.TransactionId.Value;
                    var seq = _noteSequence;
                    _ = SaveNoteReturnedAsync(txnId, seq, returnedValue);
                }
            }

            try { _svc.EscrowReturn(); } catch { }
        }

        private void UpdateConversionUI()
        {
            if (_totalForeign < 0) _totalForeign = 0;

            TxtInsertedForeign.Text = $"{_totalForeign:0.00} {_ctl.State.FromCurrency}";
            double exactMyr = _totalForeign * _ctl.State.RateToMyr;
            TxtEquivalentMyr.Text = $"RM {exactMyr:0.00}";

            double roundedMyr = Math.Floor(exactMyr);
            if (roundedMyr > _maxMyrAvailable) roundedMyr = _maxMyrAvailable;

            TxtPayableMyr.Text = $"RM {roundedMyr:0}";
            _ctl.State.FromAmount = _totalForeign;
            _ctl.State.MyrAmount = roundedMyr;
        }

        private async void Back_Click(object sender, RoutedEventArgs e)
        {
            if (_totalForeign <= 0)
            {
                BackRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            bool confirmed = CustomDialog.ShowQuestion(
                L10n.T("Mx_CancelConfirmTitle", "Cancel this transaction?"),
                string.Format(L10n.T("Mx_CancelConfirmBody", "You've already inserted {0:0.00} {1} ({2}). If you cancel now, we'll print a slip so you can collect this amount at the counter."), _totalForeign, _ctl.State.FromCurrency, TxtEquivalentMyr.Text),
                L10n.T("Mx_CancelConfirmYes", "Yes, Cancel"),
                L10n.T("Mx_CancelConfirmNo", "No, Continue"));

            if (!confirmed) return;

            try { _svc.EnableAcceptance(false); } catch { }
            PrintCancelSlip();

            if (_ctl.State.TransactionId.HasValue)
            {
                try { await _api.CompleteTransactionAsync(_ctl.State.TransactionId.Value, "Cancelled"); }
                catch (Exception ex) { KioskLocalLogger.LogError("CashIn", "Failed to mark transaction cancelled: " + ex.Message); }
            }

            _ = _api.LogJourneyEventAsync(_ctl.State.SessionId, "MoneyExchange", "StepAbandoned", "CashIn",
                outcome: "Failure", details: $"Cancelled by customer after inserting {_totalForeign:0.00} {_ctl.State.FromCurrency}",
                transactionId: _ctl.State.TransactionId);

            await Task.Delay(300);
            ExitRequested?.Invoke(this, EventArgs.Empty);
        }

        private void PrintCancelSlip()
        {
            try
            {
                var s = _ctl.State;
                var custName = s.Customer?.FullName ?? "Walk-in Customer";
                var maskedDoc = ReceiptFormatter.MaskDocumentNo(s.Customer?.IdNo);
                // By the time a cancel slip can print, at least one note was
                // accepted, which means ReceiptNo was already set by
                // SaveNoteAcceptedAsync - falls back to the transaction ID
                // only in the unexpected case it's somehow still empty.
                var receiptNo = !string.IsNullOrWhiteSpace(s.ReceiptNo) ? s.ReceiptNo : ReceiptFormatter.BuildReceiptNo(s.TransactionId, KioskAuthService.CachedKioskIdOrNull ?? "UNKNOWN");

                var r = new StringBuilder();
                r.Append(ReceiptFormatter.BuildHeader("CASH"));
                r.Append(BixolonPrinterService.ESC_BOLD_ON); r.Append("*** COUNTER COLLECTION SLIP ***\n"); r.Append(BixolonPrinterService.ESC_BOLD_OFF);
                r.Append(ReceiptFormatter.BuildCustomerBlock(receiptNo, custName, maskedDoc));

                r.Append(BixolonPrinterService.ESC_ALIGN_LEFT);
                r.Append("Status: TRANSACTION CANCELLED BY CUSTOMER\n");
                r.Append($"Inserted: {_totalForeign:0.00} {s.FromCurrency}\n");
                r.Append("--------------------------------\n");
                r.Append(BixolonPrinterService.ESC_ALIGN_CENTER); r.Append(BixolonPrinterService.ESC_BOLD_ON);
                r.Append("AMOUNT OWED TO CUSTOMER\n"); r.Append(BixolonPrinterService.ESC_DOUBLE_SIZE);
                r.Append($"RM {(_totalForeign * s.RateToMyr):0.00}\n"); r.Append(BixolonPrinterService.ESC_NORMAL_SIZE); r.Append(BixolonPrinterService.ESC_BOLD_OFF);

                r.Append(ReceiptFormatter.BuildFooter(success: false));

                _printerSvc?.PrintReceipt(r.ToString());
            }
            catch (Exception ex)
            {
                CustomDialog.ShowError(L10n.T("Mx_PrintErrorTitle", "Print Error"), ex.Message);
            }
        }

        private async void Next_Click(object sender, RoutedEventArgs e)
        {
            if (!BtnNext.IsEnabled) return;
            try { _svc.EnableAcceptance(false); } catch { }

            DoneOverlay.Visibility = Visibility.Visible;
            _ctl.State.CashInsertedMyr = _ctl.State.MyrAmount;

            // Cash-in is done - push the final totals to the transaction
            // record now, before moving on. KSK_MirrorToMcTransaction reads
            // these same columns later at completion time; without this
            // call the row (and everything mirrored from it) stays at the
            // 0 values it was created with on the first note accepted.
            if (_ctl.State.TransactionId.HasValue)
            {
                try
                {
                    await _api.UpdateTransactionAmountsAsync(
                        _ctl.State.TransactionId.Value,
                        (decimal)_ctl.State.FromAmount,
                        (decimal)_ctl.State.MyrAmount,
                        (decimal)_ctl.State.CashInsertedMyr);
                    KioskLocalLogger.LogInfo("CashIn", $"Amounts updated for Transaction {_ctl.State.TransactionId}: {_ctl.State.FromAmount} {_ctl.State.FromCurrency} -> RM {_ctl.State.MyrAmount}");
                }
                catch (Exception ex)
                {
                    // Same fire-and-forget philosophy as the rest of this
                    // step - cash is already committed, a logging/DB hiccup
                    // here shouldn't stop the customer from getting their
                    // money. But this one matters more than most, since a
                    // failure here means the eventual Mc_ mirror will still
                    // be wrong even if everything else succeeds - logged
                    // clearly so it's findable.
                    KioskLocalLogger.LogError("CashIn", $"Failed to update transaction amounts for {_ctl.State.TransactionId}: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                }
            }

            // NOTE: Completion status here is "InProgress -> still open" -
            // the transaction gets marked Completed once dispensing actually
            // succeeds in FinalReceiptStep, not here. This step only
            // confirms cash-in is done, not that MYR has been handed over yet.

            _ = _api.LogJourneyEventAsync(_ctl.State.SessionId, "MoneyExchange", "StepCompleted", "CashIn",
                outcome: "Success", details: $"Inserted {_ctl.State.FromAmount:0.00} {_ctl.State.FromCurrency} -> RM {_ctl.State.MyrAmount:0.00}",
                transactionId: _ctl.State.TransactionId);

            await Task.Delay(1500);
            NextRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}