using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OmniKiosk.Wpf.Services.MoneyExchange;

namespace OmniKiosk.Wpf.Views.MoneyExchange.Steps
{
    public partial class CurrencySelectionStep : UserControl, IStepNav
    {
        private readonly MoneyExchangeFlowController _ctl;
        public event EventHandler? NextRequested;
        public event EventHandler? BackRequested;
        public event EventHandler? ExitRequested;

        private CurrencyDisplayOption? _selected;

        public CurrencySelectionStep(MoneyExchangeFlowController ctl)
        {
            InitializeComponent();
            _ctl = ctl;
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            TitleText.Text = L10n.T("Mx_SelectCurrency", "Select currency");
            SubtitleText.Text = L10n.T("Mx_SelectCurrencySubtitle", "Choose the foreign currency you'd like to exchange for Malaysian Ringgit");
            BtnBack.Content = L10n.T("Mx_Back", "Back");
            BtnNext.Content = L10n.T("Mx_Next", "Next");

            var currencies = new List<CurrencyDisplayOption>
            {
                new() { Code = "USD", CountryName = "US Dollar", RateToMyr = 4.75, FlagUri = FlagUri("us") },
                new() { Code = "SGD", CountryName = "Singapore Dollar", RateToMyr = 3.52, FlagUri = FlagUri("sg") },
                new() { Code = "EUR", CountryName = "Euro", RateToMyr = 5.12, FlagUri = FlagUri("eu") },
                new() { Code = "GBP", CountryName = "British Pound", RateToMyr = 6.01, FlagUri = FlagUri("gb") },
                new() { Code = "AUD", CountryName = "Australian Dollar", RateToMyr = 3.10, FlagUri = FlagUri("au") },
                new() { Code = "JPY", CountryName = "Japanese Yen", RateToMyr = 0.032, FlagUri = FlagUri("jp") },
                new() { Code = "IDR", CountryName = "Indonesian Rupiah", RateToMyr = 0.000232, FlagUri = FlagUri("id") },
                new() { Code = "CNY", CountryName = "Chinese Yuan", RateToMyr = 0.572, FlagUri = FlagUri("cn") }
            };
            foreach (var c in currencies) c.RateDisplay = $"1 = RM {c.RateToMyr:0.00##}";
            LstCurrencies.ItemsSource = currencies;
        }

        private static Uri FlagUri(string isoCode) => new($"pack://application:,,,/Assets/Flags/{isoCode}.svg");

        private void CurrencyCard_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Border card || card.Tag is not CurrencyDisplayOption option) return;

            _selected = option;
            BtnNext.IsEnabled = true;

            foreach (var item in LstCurrencies.Items)
            {
                if (LstCurrencies.ItemContainerGenerator.ContainerFromItem(item) is ContentPresenter presenter)
                {
                    if (VisualTreeHelperFindBorder(presenter) is Border b)
                    {
                        bool isThis = ReferenceEquals(item, option);
                        b.BorderBrush = isThis
                            ? (Brush)Application.Current.Resources["PrimaryBrush"]
                            : (Brush)Application.Current.Resources["BorderBrush"];
                        b.BorderThickness = new Thickness(isThis ? 2.5 : 1);
                        b.Background = isThis
                            ? (Brush)Application.Current.Resources["PrimarySurfaceBrush"]
                            : (Brush)Application.Current.Resources["CardBrush"];
                    }
                }
            }
        }

        private static Border? VisualTreeHelperFindBorder(DependencyObject root)
        {
            if (root is Border b) return b;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var found = VisualTreeHelperFindBorder(VisualTreeHelper.GetChild(root, i));
                if (found != null) return found;
            }
            return null;
        }

        private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) return;
            _ctl.State.FromCurrency = _selected.Code;
            _ctl.State.RateToMyr = _selected.RateToMyr;
            NextRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public class CurrencyDisplayOption
    {
        public string Code { get; set; } = "";
        public string CountryName { get; set; } = "";
        public double RateToMyr { get; set; }
        public string RateDisplay { get; set; } = "";
        public Uri? FlagUri { get; set; }
    }
}