using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.IO;
using System.Windows.Media.Imaging;
using OmniKiosk.Wpf.Controls;
using OmniKiosk.Wpf.Services.MoneyExchange;
using OmniKiosk.Wpf.Sdk.Printer;
using OmniKiosk.Wpf.Sdk.Dispenser;
using OmniKiosk.Wpf.Services;
using System.Threading.Tasks;
using System.Linq;

namespace OmniKiosk.Wpf.Views.MoneyExchange.Steps
{
    public partial class FinalReceiptStep : UserControl, IStepNav
    {
        private readonly MoneyExchangeFlowController _ctl;
        private readonly BixolonPrinterService _printerSvc = GlobalHardwareManager.Printer;
        private readonly PuloonDispenserService _dispenserSvc = GlobalHardwareManager.MoneyDispenser;
        private readonly MoneyExchangeApiClient _api = new();

        public event EventHandler? NextRequested;
        public event EventHandler? BackRequested;
        public event EventHandler? ExitRequested;

        private bool _dispenseSuccessful = false;
        private string _dispenseErrorMsg = "";

        public FinalReceiptStep(MoneyExchangeFlowController ctl)
        {
            InitializeComponent();
            _ctl = ctl;
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            // ID/passport retrieval gate - there is no physical sensor on
            // this hardware to detect whether the document was actually
            // taken back (confirmed earlier - the document readers have no
            // "document removed" signal), so this is a software-only
            // acknowledgment gate, not a real interlock. Shown before
            // anything else on this screen, including the pre-dispense
            // availability check, since cash should not even be attempted
            // until this is confirmed.
            bool idRetrieved = CustomDialog.ShowQuestion(
                L10n.T("Mx_RetrieveIdTitle", "Please Take Your ID / Passport"),
                L10n.T("Mx_RetrieveIdBody", "Before we dispense your cash, please make sure you have taken back your IC or passport from the reader.\n\nHave you retrieved your document?"),
                L10n.T("Mx_RetrieveIdYes", "Yes, I Have It"),
                L10n.T("Mx_RetrieveIdNo", "Not Yet"));

            while (!idRetrieved)
            {
                // Does not proceed until confirmed - re-shows the same
                // prompt rather than silently continuing, since there is
                // no hardware fallback to fall back on here.
                idRetrieved = CustomDialog.ShowQuestion(
                    L10n.T("Mx_RetrieveIdTitle", "Please Take Your ID / Passport"),
                    L10n.T("Mx_RetrieveIdBody", "Before we dispense your cash, please make sure you have taken back your IC or passport from the reader.\n\nHave you retrieved your document?"),
                    L10n.T("Mx_RetrieveIdYes", "Yes, I Have It"),
                    L10n.T("Mx_RetrieveIdNo", "Not Yet"));
            }

            TitleText.Text = L10n.T("Mx_Complete", "Transaction Complete");
            SubtitleText.Text = L10n.T("Mx_CollectCashSubtitle", "Please collect your cash from the dispenser below.");
            TotalDispensedLabel.Text = L10n.T("Mx_TotalDispensed", "TOTAL DISPENSED");
            VerifiedCustomerLabel.Text = L10n.T("Mx_VerifiedCustomer", "VERIFIED CUSTOMER");
            ForeignInsertedLabel.Text = L10n.T("Mx_ForeignInserted", "FOREIGN INSERTED");
            ExchangeRateLabel.Text = L10n.T("Mx_ExchangeRateLabel", "EXCHANGE RATE");
            BreakdownLabel.Text = L10n.T("Mx_NotesBreakdown", "Notes Dispensed Breakdown");
            BtnPrintReceipt.Content = "🖨️ " + L10n.T("Mx_PrintReceipt", "Print Receipt");
            BtnDone.Content = L10n.T("Mx_CompleteTransaction", "Complete Transaction");

            LoadTransactionData();

            _ = _api.LogJourneyEventAsync(_ctl.State.SessionId, "MoneyExchange", "StepEntered", "FinalReceipt",
                transactionId: _ctl.State.TransactionId);

            // Cross-check against the DB-configured denominations before
            // dispensing - the dispenser hardware is hardcoded to exactly 4
            // fixed cassettes (DispenseAsync always takes 4 counts), so this
            // validates the DB agrees with that rather than trying to make
            // the physical dispense call itself dynamic. If the DB ever has
            // something other than exactly [100,50,10,1], that's a
            // configuration problem worth knowing about, not something to
            // silently paper over - falls back to the values already
            // calculated locally either way, so a failed API call never
            // blocks a customer from getting their cash.
            try
            {
                var apiDenoms = await _api.GetDenominationsAsync("MYR");
                var expected = new[] { 100, 50, 10, 1 };
                var actual = apiDenoms.Select(d => d.DenominationValue).ToArray();
                if (!expected.SequenceEqual(actual))
                {
                    KioskLocalLogger.LogError("FinalReceipt",
                        $"DB denominations [{string.Join(",", actual)}] don't match the 4 physical cassettes [{string.Join(",", expected)}] - dispensing with the hardcoded breakdown regardless, but this is worth fixing in Ksk_BanknoteDenominations.");
                }
            }
            catch (Exception ex)
            {
                KioskLocalLogger.LogError("FinalReceipt", "Could not reach denominations API, continuing with local calculation: " + ex.Message);
            }

            // 🚀 FIX: Mapped exactly to your physical cassette order (Top to Bottom)
            int c1 = int.Parse(Txt1.Text);   // Cassette 1 (Top)    = RM 1
            int c2 = int.Parse(Txt10.Text);  // Cassette 2          = RM 10
            int c3 = int.Parse(Txt50.Text);  // Cassette 3          = RM 50
            int c4 = int.Parse(Txt100.Text); // Cassette 4 (Bottom) = RM 100

            if (c1 > 0 || c2 > 0 || c3 > 0 || c4 > 0)
            {
                // Pre-dispense availability check, now blocking - KioskId
                // is the real, server-resolved identifier as of this
                // change (KioskAuthService.GetKioskIdAsync, resolved from
                // Ksk_Terminals by this machine's MAC address), not the
                // "K1" placeholder that made this check unsafe to trust
                // before. A genuine "insufficient" result here now stops
                // the dispense attempt rather than just being logged.
                try
                {
                    var kioskId = await KioskAuthService.GetKioskIdAsync();
                    var availability = await _api.GetDenominationBreakdownAsync(kioskId, (decimal)_ctl.State.MyrAmount);
                    if (!availability.CanDispenseFully)
                    {
                        _dispenseSuccessful = false;
                        _dispenseErrorMsg = L10n.T("Mx_InsufficientCash", "Insufficient cash available at this kiosk");
                        ShowDispenserNotice();
                        await CompleteTransactionSafeAsync("DispenseFailed");
                        PrintReceipt();
                        return;
                    }
                }
                catch (Exception ex)
                {
                    // Availability check itself being unreachable is still
                    // treated as "proceed anyway" - the actual hardware
                    // dispense call below has its own failure handling, and
                    // an unreachable check should degrade to "try", not
                    // "assume the worst and always block".
                    KioskLocalLogger.LogError("FinalReceipt", "Denomination availability check failed, proceeding without it: " + ex.Message);
                }

                if (!_dispenserSvc.IsConnected)
                {
                    string foundPort = await _dispenserSvc.AutoDetectDispenserPortAsync();

                    if (string.IsNullOrEmpty(foundPort))
                    {
                        _dispenseSuccessful = false;
                        _dispenseErrorMsg = L10n.T("Mx_HardwareOffline", "Hardware Offline (Check USB Cable/Power)");
                        ShowDispenserNotice();
                        await CompleteTransactionSafeAsync("DispenseFailed");
                        PrintReceipt();
                        _ = _api.LogJourneyEventAsync(_ctl.State.SessionId, "MoneyExchange", "StepFailed", "FinalReceipt",
                            outcome: "Error", details: "Dispenser hardware offline", transactionId: _ctl.State.TransactionId);
                        return;
                    }
                }

                var response = await _dispenserSvc.DispenseAsync(c1, c2, c3, c4);

                if (response.Success)
                {
                    _dispenseSuccessful = true;
                    await CompleteTransactionSafeAsync("Completed");
                    await RecordDispensedNotesAsync(c1, c2, c3, c4);
                    PrintReceipt();
                    _ = _api.LogJourneyEventAsync(_ctl.State.SessionId, "MoneyExchange", "StepCompleted", "FinalReceipt",
                        outcome: "Success", transactionId: _ctl.State.TransactionId);
                }
                else
                {
                    _dispenseSuccessful = false;
                    _dispenseErrorMsg = response.Message;
                    ShowDispenserNotice();
                    await CompleteTransactionSafeAsync("DispenseFailed");
                    PrintReceipt();
                    _ = _api.LogJourneyEventAsync(_ctl.State.SessionId, "MoneyExchange", "StepFailed", "FinalReceipt",
                        outcome: "Failure", details: "Dispense failed: " + _dispenseErrorMsg, transactionId: _ctl.State.TransactionId);
                }
            }
            else
            {
                _dispenseSuccessful = true;
                await CompleteTransactionSafeAsync("Completed");
                PrintReceipt();
                _ = _api.LogJourneyEventAsync(_ctl.State.SessionId, "MoneyExchange", "StepCompleted", "FinalReceipt",
                    outcome: "Success", details: "No cash to dispense (zero-value transaction)", transactionId: _ctl.State.TransactionId);
            }
        }

        // Reuses the exact same RecordNoteAsync / KSK_RecordTransactionNote
        // path CashInStep already calls for accepted notes - same table
        // (Ksk_TransactionNotes), same one-row-per-physical-note
        // convention (confirmed from real data: a transaction's notes are
        // stored as multiple individual rows, not one row with a count).
        // Outcome="Dispensed", CurrencyCode="MYR" for the dispense side.
        //
        // Deliberately NOT called from the DispenseFailed branch above -
        // if the dispenser reported failure, how many notes (if any)
        // actually came out isn't known, so recording a full breakdown as
        // "Dispensed" would be recording something that may not have
        // happened. That's a separate, harder reconciliation problem, not
        // something to guess at here.
        private async Task RecordDispensedNotesAsync(int count1, int count10, int count50, int count100)
        {
            if (!_ctl.State.TransactionId.HasValue)
            {
                KioskLocalLogger.LogError("FinalReceipt", "RecordDispensedNotesAsync called with no TransactionId set - nothing recorded.");
                return;
            }

            long transactionId = _ctl.State.TransactionId.Value;
            //int sequenceNo = 1;
            int sequenceNo =
                _ctl.State.LastTransactionNoteSequence + 1;
            var denominationCounts = new (int Value, int Count)[]
            {
                (100, count100),
                (50,  count50),
                (10,  count10),
                (1,   count1),
            };
            foreach (var (value, count) in denominationCounts)
            {
                for (int i = 0; i < count; i++)
                {
                    int currentSequence = sequenceNo++;

                    try
                    {
                        await _api.RecordNoteAsync(
                            transactionId,
                            currentSequence,
                            "MYR",
                            value,
                            "Dispensed");

                        _ctl.State.LastTransactionNoteSequence =
                            currentSequence;
                    }
                    catch (Exception ex)
                    {
                        // Never reuse a failed sequence number for another physical note.
                        _ctl.State.LastTransactionNoteSequence =
                            currentSequence;

                        KioskLocalLogger.LogError(
                            "FinalReceipt",
                            $"Failed to record dispensed note #{currentSequence} " +
                            $"(RM{value}) for transaction {transactionId}: {ex.Message}");
                    }
                }
            }
            //foreach (var (value, count) in denominationCounts)
            //{
            //    for (int i = 0; i < count; i++)
            //    {
            //        try
            //        {
            //            await _api.RecordNoteAsync(transactionId, sequenceNo, "MYR", (decimal)value, "Dispensed");
            //            sequenceNo++;
            //        }
            //        catch (Exception ex)
            //        {
            //            // Cash has already physically dispensed by this point -
            //            // same posture as CompleteTransactionSafeAsync: a
            //            // failed API call here must never be shown to the
            //            // customer or block the flow, only logged.
            //            KioskLocalLogger.LogError("FinalReceipt",
            //                $"Failed to record dispensed note #{sequenceNo} (RM{value}) for transaction {transactionId}: {ex.Message}");
            //        }
            //    }
            //}
        }

        // Cash has already physically moved (dispensed or not) by the time
        // this runs - a failed API call here must never be shown to the
        // customer or change what already happened at the machine.
        private async Task CompleteTransactionSafeAsync(string status)
        {
            if (!_ctl.State.TransactionId.HasValue)
            {
                KioskLocalLogger.LogError("FinalReceipt", $"CompleteTransactionSafeAsync({status}) called with no TransactionId set - nothing sent to the API at all.");
                return;
            }

            try
            {
                await _api.CompleteTransactionAsync(_ctl.State.TransactionId.Value, status);
                KioskLocalLogger.LogInfo("FinalReceipt", $"Transaction {_ctl.State.TransactionId} marked {status}");
            }
            catch (Exception ex)
            {
                KioskLocalLogger.LogError("FinalReceipt", $"Failed to mark transaction {_ctl.State.TransactionId} as {status}: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void ShowDispenserNotice()
        {
            CustomDialog.ShowWarning(
                L10n.T("Mx_DispenserNoticeTitle", "Dispenser Notice"),
                string.Format(L10n.T("Mx_DispenserNoticeBody", "The machine could not dispense the cash.\nReason: {0}\n\nPlease print your Counter Slip and proceed to the counter."), _dispenseErrorMsg));
        }

        private void LoadTransactionData()
        {
            var s = _ctl.State;
            TxtCustomer.Text = s.Customer?.FullName ?? "Walk-in";
            TxtForeign.Text = $"{s.FromAmount:0.00} {s.FromCurrency}";
            TxtRate.Text = $"{s.RateToMyr:0.0000}";
            TxtMyr.Text = $"RM {s.MyrAmount:0}";

            if (!string.IsNullOrWhiteSpace(s.LiveFaceImageBase64))
            {
                try
                {
                    var bytes = Convert.FromBase64String(s.LiveFaceImageBase64);
                    using var ms = new MemoryStream(bytes);
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    LiveFaceImage.Source = bmp;
                }
                catch { }
            }

            CalculateDispenserNotes((int)s.MyrAmount);
        }

        private void CalculateDispenserNotes(int totalMyr)
        {
            int remaining = totalMyr;
            int count100 = remaining / 100; remaining %= 100;
            int count50 = remaining / 50; remaining %= 50;
            int count10 = remaining / 10; remaining %= 10;
            int count1 = remaining / 1;

            Txt100.Text = count100.ToString();
            Txt50.Text = count50.ToString();
            Txt10.Text = count10.ToString();
            Txt1.Text = count1.ToString();

            Row100.Visibility = count100 > 0 ? Visibility.Visible : Visibility.Collapsed;
            Row50.Visibility = count50 > 0 ? Visibility.Visible : Visibility.Collapsed;
            Row10.Visibility = count10 > 0 ? Visibility.Visible : Visibility.Collapsed;
            Row1.Visibility = count1 > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void PrintReceipt_Click(object sender, RoutedEventArgs e) => PrintReceipt();

        private void PrintReceipt()
        {
            try
            {
                var s = _ctl.State;
                var custName = s.Customer?.FullName ?? "Walk-in Customer";
                var maskedDoc = ReceiptFormatter.MaskDocumentNo(s.Customer?.IdNo);
                var receiptNo = !string.IsNullOrWhiteSpace(s.ReceiptNo) ? s.ReceiptNo : ReceiptFormatter.BuildReceiptNo(s.TransactionId, KioskAuthService.CachedKioskIdOrNull ?? "UNKNOWN");

                var r = new StringBuilder();

                if (_dispenseSuccessful)
                {
                    r.Append(ReceiptFormatter.BuildHeader("CASH"));
                    r.Append(ReceiptFormatter.BuildCustomerBlock(receiptNo, custName, maskedDoc));

                    r.Append(BixolonPrinterService.ESC_ALIGN_CENTER);

                    r.Append(BixolonPrinterService.ESC_BOLD_ON);
                    r.Append("FOREIGN INSERTED\n");
                    r.Append(BixolonPrinterService.ESC_BOLD_OFF);
                    r.Append($"{s.FromAmount:0.00} {s.FromCurrency}\n\n");

                    r.Append(BixolonPrinterService.ESC_BOLD_ON);
                    r.Append("EXCHANGE RATE\n");
                    r.Append(BixolonPrinterService.ESC_BOLD_OFF);
                    r.Append($"{s.RateToMyr:0.0000}\n\n");

                    r.Append(BixolonPrinterService.ESC_BOLD_ON);
                    r.Append("TOTAL DISPENSED\n");
                    r.Append(BixolonPrinterService.ESC_DOUBLE_SIZE);
                    r.Append($"RM {s.MyrAmount:0.00}\n\n");
                    r.Append(BixolonPrinterService.ESC_NORMAL_SIZE);
                    r.Append(BixolonPrinterService.ESC_BOLD_OFF);

                    r.Append("--------------------------------\n");
                    r.Append(BixolonPrinterService.ESC_BOLD_ON);
                    r.Append("NOTES DISPENSED\n");
                    r.Append(BixolonPrinterService.ESC_BOLD_OFF);

                    int remaining = (int)s.MyrAmount;
                    int count100 = remaining / 100; remaining %= 100;
                    int count50 = remaining / 50; remaining %= 50;
                    int count10 = remaining / 10; remaining %= 10;
                    int count1 = remaining / 1;

                    if (count100 > 0) r.Append($"RM 100  x {count100}\n");
                    if (count50 > 0) r.Append($"RM 50   x {count50}\n");
                    if (count10 > 0) r.Append($"RM 10   x {count10}\n");
                    if (count1 > 0) r.Append($"RM 1    x {count1}\n");

                    r.Append(ReceiptFormatter.BuildFooter(success: true));
                }
                else
                {
                    r.Append(ReceiptFormatter.BuildHeader("CASH"));
                    r.Append(BixolonPrinterService.ESC_ALIGN_CENTER);
                    r.Append(BixolonPrinterService.ESC_BOLD_ON);
                    r.Append("*** COUNTER COLLECTION SLIP ***\n");
                    r.Append(BixolonPrinterService.ESC_BOLD_OFF);
                    r.Append(ReceiptFormatter.BuildCustomerBlock(receiptNo, custName, maskedDoc));

                    r.Append(BixolonPrinterService.ESC_ALIGN_CENTER);
                    r.Append("Status: MACHINE DISPENSE FAILED\n");
                    r.Append($"Reason: {_dispenseErrorMsg}\n");
                    r.Append("--------------------------------\n");
                    r.Append(BixolonPrinterService.ESC_BOLD_ON);
                    r.Append("AMOUNT OWED TO CUSTOMER\n");
                    r.Append(BixolonPrinterService.ESC_DOUBLE_SIZE);
                    r.Append($"RM {s.MyrAmount:0.00}\n");
                    r.Append(BixolonPrinterService.ESC_NORMAL_SIZE);
                    r.Append(BixolonPrinterService.ESC_BOLD_OFF);

                    r.Append(ReceiptFormatter.BuildFooter(success: false));
                }

                if (_printerSvc == null || !_printerSvc.PrintReceipt(r.ToString()))
                    CustomDialog.ShowError(L10n.T("Mx_PrintErrorTitle", "Print Error"), L10n.T("Mx_PrintErrorBody", "Failed to print. Check if the printer has paper and is connected."));
            }
            catch (Exception ex)
            {
                CustomDialog.ShowError(L10n.T("Mx_PrintErrorTitle", "Print Error"), ex.Message);
            }
        }
        private void Done_Click(object sender, RoutedEventArgs e)
        {
            _ = _api.LogJourneyEventAsync(_ctl.State.SessionId, "MoneyExchange", "SessionEnd", "FinalReceipt",
                outcome: _dispenseSuccessful ? "Success" : "Failure", transactionId: _ctl.State.TransactionId);
            ExitRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}