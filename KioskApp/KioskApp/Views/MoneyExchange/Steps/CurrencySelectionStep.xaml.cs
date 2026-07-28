using System;
using System.Collections.Generic;
using System.Linq;
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
        private readonly MoneyExchangeApiClient _api = new();
        public event EventHandler? NextRequested;
        public event EventHandler? BackRequested;
        public event EventHandler? ExitRequested;

        private CurrencyDisplayOption? _selected;

        public CurrencySelectionStep(MoneyExchangeFlowController ctl)
        {
            InitializeComponent();
            _ctl = ctl;
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            TitleText.Text = L10n.T("Mx_SelectCurrency", "Select currency");
            SubtitleText.Text = L10n.T("Mx_SelectCurrencySubtitle", "Choose the foreign currency you'd like to exchange for Malaysian Ringgit");
            BtnBack.Content = L10n.T("Mx_Back", "Back");
            BtnNext.Content = L10n.T("Mx_Next", "Next");

            await LoadCurrenciesAsync();
        }

        // Was a hardcoded List<CurrencyDisplayOption> before - now comes
        // from GET /api/v1/Currencies, joined with each currency's active
        // rate. Kept as its own method (rather than inline in Loaded) so
        // BtnRetry_Click (added below) can call it again without duplicating
        // this logic.
        private async System.Threading.Tasks.Task LoadCurrenciesAsync()
        {
            LstCurrencies.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Collapsed;
            LoadingPanel.Visibility = Visibility.Visible;

            try
            {
                var apiCurrencies = await _api.GetCurrenciesAsync();

                var currencies = apiCurrencies.Select(c => new CurrencyDisplayOption
                {
                    Code = c.CurrencyCode,
                    CountryName = c.CurrencyName,
                    RateToMyr = (double)c.BuyRate,
                    FlagUri = FlagUri(c.FlagCountryCode ?? c.CurrencyCode.ToLowerInvariant())
                }).ToList();

                foreach (var c in currencies) c.RateDisplay = $"1 = RM {c.RateToMyr:0.00##}";

                LstCurrencies.ItemsSource = currencies;
                LoadingPanel.Visibility = Visibility.Collapsed;
                LstCurrencies.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                // Network down, API not running, DB unreachable - whatever it
                // is, the customer needs a clear "can't continue" state, not
                // a silently empty currency list.
                System.Diagnostics.Debug.WriteLine("Failed to load currencies: " + ex.Message);
                LoadingPanel.Visibility = Visibility.Collapsed;
                ErrorPanel.Visibility = Visibility.Visible;
            }
        }

        private async void BtnRetryLoad_Click(object sender, RoutedEventArgs e) => await LoadCurrenciesAsync();

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
