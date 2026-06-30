using System;
using System.Windows;
using System.Windows.Controls;
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
            ChkAgree.IsChecked = false; BtnNext.IsEnabled = false;
        }
        private void ChkAgree_Checked(object sender, RoutedEventArgs e) => BtnNext.IsEnabled = ChkAgree.IsChecked == true;
        private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
        private void Next_Click(object sender, RoutedEventArgs e) => NextRequested?.Invoke(this, EventArgs.Empty);
    }
}