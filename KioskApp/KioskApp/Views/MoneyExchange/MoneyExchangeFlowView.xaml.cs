using System;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using OmniKiosk.Wpf.Services.MoneyExchange;
using OmniKiosk.Wpf.Views.MoneyExchange.Steps;

namespace OmniKiosk.Wpf.Views.MoneyExchange
{
    public partial class MoneyExchangeFlowView : UserControl
    {
        public event EventHandler? BackRequested;
        private readonly MoneyExchangeFlowController _ctl = new MoneyExchangeFlowController();
        private int _stepIndex = 0;

        public MoneyExchangeFlowView(string currentLanguage)
        {
            InitializeComponent();
            Thread.CurrentThread.CurrentUICulture = new CultureInfo(currentLanguage == "ms" ? "ms" : "en");
            Thread.CurrentThread.CurrentCulture = Thread.CurrentThread.CurrentUICulture;
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            TitleText.Text = L10n.T("Mx_Title", "Money Exchange");
            GoToStep(0);
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e) { }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (_stepIndex == 0) { BackRequested?.Invoke(this, EventArgs.Empty); return; }
            GoToStep(_stepIndex - 1);
        }

        private void GoToStep(int idx)
        {
            _stepIndex = idx;
            StepText.Text = $"{L10n.T("Mx_Title", "Money Exchange")} • Step {idx + 1}/6";

            UserControl step = idx switch
            {
                0 => new CurrencySelectionStep(_ctl),
                1 => new TermsAndConditionsStep(_ctl),
                2 => new CustomerDetailsStep(_ctl),
                3 => new FaceVerificationStep(_ctl),
                4 => new CashInStep(_ctl),
                _ => new FinalReceiptStep(_ctl),
            };

            if (step is IStepNav nav)
            {
                nav.NextRequested += (_, __) => GoToStep(Math.Min(5, _stepIndex + 1));
                nav.BackRequested += (_, __) => GoToStep(Math.Max(0, _stepIndex - 1));
                nav.ExitRequested += (_, __) => BackRequested?.Invoke(this, EventArgs.Empty);
            }
            Host.Content = step;
        }
    }

    internal interface IStepNav
    {
        event EventHandler? NextRequested;
        event EventHandler? BackRequested;
        event EventHandler? ExitRequested;
    }
}