using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using OmniKiosk.Wpf.Controls;
using OmniKiosk.Wpf.Services.MoneyExchange;

namespace OmniKiosk.Wpf.Views.MoneyExchange.Steps
{
    public partial class TermsAndConditionsStep : UserControl, IStepNav
    {
        private readonly MoneyExchangeFlowController _ctl;
        public event EventHandler? NextRequested;
        public event EventHandler? BackRequested;
        public event EventHandler? ExitRequested;

        public TermsAndConditionsStep(MoneyExchangeFlowController ctl)
        {
            InitializeComponent();
            _ctl = ctl;
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            TitleText.Text = L10n.T("Mx_Terms_Title", "Terms and Conditions");
            SubtitleText.Text = L10n.T("Mx_Terms_Subtitle", "Please read carefully before continuing");
            ConfirmHeaderText.Text = L10n.T("Mx_Terms_ConfirmHeader", "By continuing, you confirm that:");
            BtnBack.Content = L10n.T("Mx_Back", "Back");
            BtnNext.Content = L10n.T("Mx_Next", "Next");

            TermsList.ItemsSource = new List<TermItem>
            {
                new("🏛️", L10n.T("Mx_Terms_Bnm", "This transaction is conducted under a licensed money-changing service regulated by Bank Negara Malaysia (BNM).")),
                new("⚠️", L10n.T("Mx_Terms_Reporting", "We are required by law to report suspicious transactions to the relevant authorities.")),
                new("💱", L10n.T("Mx_Terms_Rate", "The exchange rate shown is confirmed at the point you accept it, and may change if the transaction is not completed.")),
                new("🪪", L10n.T("Mx_Terms_Id", "A valid passport or identity card is required, and identity verification may include a biometric or electronic KYC check.")),
                new("💵", L10n.T("Mx_Terms_Notes", "All notes inserted are checked by an automated validator. Notes that cannot be verified will be returned, not accepted.")),
                new("🚫", L10n.T("Mx_Terms_NoReversal", "Once Ringgit has been dispensed, the transaction cannot be reversed.")),
                new("🗄️", L10n.T("Mx_Terms_Retention", "Transaction and identification records are retained in line with BNM and anti-money-laundering record-keeping requirements.")),
                new("🧾", L10n.T("Mx_Terms_Receipt", "Please keep your receipt for your records.")),
            };

            ConfirmList.ItemsSource = new List<string>
            {
                L10n.T("Mx_Terms_ConfirmAccurate", "The information you provide during this transaction is true and accurate"),
                L10n.T("Mx_Terms_ConfirmUnderstood", "You have read and understood these terms"),
                L10n.T("Mx_Terms_ConfirmComply", "You agree to comply with all applicable regulations"),
            };
        }

        private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

        // No checkbox anymore - Next always opens the confirmation dialog
        // directly. "Yes" advances, "No" leaves the customer right where they
        // are so they can re-read before deciding again.
        private void Next_Click(object sender, RoutedEventArgs e)
        {
            bool agreed = CustomDialog.ShowQuestion(
                L10n.T("Mx_Terms_ConfirmDialogTitle", "Do you agree to the Terms and Conditions?"),
                L10n.T("Mx_Terms_ConfirmDialogBody", "Please confirm you have read and agree to the terms and conditions shown."),
                L10n.T("Mx_Terms_ConfirmDialogYes", "Yes, I Agree"),
                L10n.T("Mx_Terms_ConfirmDialogNo", "No"));

            if (agreed)
                NextRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public record TermItem(string Icon, string Text);
}

