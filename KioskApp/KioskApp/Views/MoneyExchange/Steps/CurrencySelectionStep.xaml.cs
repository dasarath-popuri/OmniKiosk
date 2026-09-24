using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OmniKiosk.Wpf.Controls;
using OmniKiosk.Wpf.Services;
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
        private decimal? _selectedForeignAmount;
        private decimal _roundedMyrAmount;

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
            BtnOtherAmount.Content = L10n.T("Mx_OtherAmount", "Or enter a custom amount...");

            await LoadCurrenciesAsync();
        }

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
                System.Diagnostics.Debug.WriteLine("Failed to load currencies: " + ex.Message);
                LoadingPanel.Visibility = Visibility.Collapsed;
                ErrorPanel.Visibility = Visibility.Visible;
            }
        }

        private async void BtnRetryLoad_Click(object sender, RoutedEventArgs e) => await LoadCurrenciesAsync();

        // Restored exactly to your original local SVG pack URI implementation
        private static Uri FlagUri(string isoCode) => new($"pack://application:,,,/Assets/Flags/{isoCode}.svg");

        private void CurrencyCard_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Border card || card.Tag is not CurrencyDisplayOption option) return;

            _selected = option;
            _selectedForeignAmount = null;
            SelectedAmountPanel.Visibility = Visibility.Collapsed;

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

            AmountEntryLabel.Text = L10n.T("Mx_HowMuchToExchange", "How much would you like to exchange?");
            AmountEntryPanel.Visibility = Visibility.Visible;
            AmountLimitWarningPanel.Visibility = Visibility.Collapsed;
            BtnNext.IsEnabled = false;

            BuildPresetAmountButtons(option);
        }

        private static readonly Dictionary<string, decimal[]> PresetAmountsByCurrency = new(StringComparer.OrdinalIgnoreCase)
        {
            ["USD"] = new decimal[] { 50, 100, 200, 500 },
            ["SGD"] = new decimal[] { 50, 100, 200, 500 },
            ["EUR"] = new decimal[] { 50, 100, 200, 500 },
            ["GBP"] = new decimal[] { 50, 100, 200, 500 },
            ["AUD"] = new decimal[] { 50, 100, 200, 500 },
            ["CNY"] = new decimal[] { 200, 500, 1000, 2000 },
            ["JPY"] = new decimal[] { 5000, 10000, 20000, 50000 },
            ["IDR"] = new decimal[] { 500000, 1000000, 2000000, 5000000 },
        };

        private void BuildPresetAmountButtons(CurrencyDisplayOption option)
        {
            PresetAmountsGrid.Children.Clear();

            decimal[] presets = PresetAmountsByCurrency.TryGetValue(option.Code, out var known)
                ? known
                : ComputeFallbackPresets((decimal)option.RateToMyr);

            foreach (var amount in presets)
            {
                string amountFormat = (amount % 1 == 0) ? "0" : "0.####";

                var btn = new Button
                {
                    Content = $"{option.Code} {amount.ToString(amountFormat)}",
                    Style = (Style)FindResource("AmountPresetButton"),
                    FontSize = 20,
                    FontWeight = FontWeights.Bold,
                    Tag = amount
                };
                btn.Click += PresetAmount_Click;
                PresetAmountsGrid.Children.Add(btn);
            }
        }

        private static decimal[] ComputeFallbackPresets(decimal rateToMyr)
        {
            if (rateToMyr <= 0) return new decimal[] { 50, 100, 200, 500 };

            decimal targetForeign = 100m / rateToMyr;
            decimal magnitude = 1;
            while (magnitude * 10 <= targetForeign) magnitude *= 10;
            while (magnitude > targetForeign && magnitude > 0.01m) magnitude /= 10;

            decimal[] niceMultiples = { 1m, 2m, 5m };
            decimal bestBase = magnitude;
            decimal bestDiff = decimal.MaxValue;
            foreach (var mult in niceMultiples)
            {
                var candidate = magnitude * mult;
                var diff = Math.Abs(candidate - targetForeign);
                if (diff < bestDiff) { bestDiff = diff; bestBase = candidate; }
            }

            return new[] { bestBase, bestBase * 2, bestBase * 5, bestBase * 10 };
        }

        private void PresetAmount_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is decimal amount)
                SetSelectedAmount(amount);
        }

        private void BtnOtherAmount_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) return;

            var result = CustomAmountDialog.Show(
                L10n.T("Mx_EnterAmountTitle", "Enter Amount"),
                _selected.Code,
                _selectedForeignAmount);

            if (result.HasValue)
                SetSelectedAmount(result.Value);
        }

        private void SetSelectedAmount(decimal foreignAmount)
        {
            if (_selected == null || foreignAmount <= 0) return;

            _selectedForeignAmount = foreignAmount;
            decimal rawMyr = foreignAmount * (decimal)_selected.RateToMyr;
            _roundedMyrAmount = Math.Round(rawMyr, 0, MidpointRounding.AwayFromZero);

            string format = (foreignAmount % 1 == 0) ? "0" : "0.##";
            SelectedForeignAmountText.Text = $"{_selected.Code} {foreignAmount.ToString(format)}";

            bool wasRounded = rawMyr != _roundedMyrAmount;
            if (wasRounded)
            {
                RawMyrText.Text = $"RM {rawMyr:0.00}";
                RawMyrText.Visibility = Visibility.Visible;
                RoundingNoteText.Text = L10n.T("Mx_RoundingNote", "Rounded to nearest Ringgit for cash dispensing.");
                RoundingNotePanel.Visibility = Visibility.Visible;
            }
            else
            {
                RawMyrText.Visibility = Visibility.Collapsed;
                RoundingNotePanel.Visibility = Visibility.Collapsed;
            }

            RoundedMyrText.Text = string.Format(L10n.T("Mx_YoullReceive", "Receive RM {0:0.00}"), _roundedMyrAmount);

            SelectedAmountPanel.Visibility = Visibility.Visible;
            AmountLimitWarningPanel.Visibility = Visibility.Collapsed;
            BtnNext.IsEnabled = true;
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

        private async void Next_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null || !_selectedForeignAmount.HasValue || _selectedForeignAmount.Value <= 0)
                return;

            decimal foreignAmount = _selectedForeignAmount.Value;
            decimal myrAmount = _roundedMyrAmount;

            BtnNext.IsEnabled = false;
            AmountLimitWarningPanel.Visibility = Visibility.Collapsed;

            try
            {
                var kioskId = await KioskAuthService.GetKioskIdAsync();
                var result = await _api.CheckPerTransactionLimitAsync(kioskId, myrAmount);

                if (!result.IsWithinLimits)
                {
                    AmountLimitWarningText.Text = string.Format(
                        L10n.T("Mx_PerTxnLimitWarning", "This exceeds the maximum of RM {0:0.00} per transaction. Please choose a smaller amount, or visit your nearest branch for a larger exchange."),
                        result.PerTxnLimit);
                    AmountLimitWarningPanel.Visibility = Visibility.Visible;
                    BtnNext.IsEnabled = true;
                    return;
                }
            }
            catch (Exception ex)
            {
                KioskLocalLogger.LogError("CurrencySelection", "Per-transaction limit check failed (blocking as a precaution): " + ex.Message);
                CustomDialog.ShowError(
                    L10n.T("Mx_LimitExceededTitle", "Unable to Proceed at This Kiosk"),
                    L10n.T("Mx_LimitCheckFailedBody", "We couldn't verify transaction limits. Please proceed to the counter for assistance."));
                ExitRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            _ctl.State.FromCurrency = _selected.Code;
            _ctl.State.RateToMyr = _selected.RateToMyr;
            _ctl.State.FromAmount = (double)foreignAmount;
            _ctl.State.MyrAmount = (double)myrAmount;

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