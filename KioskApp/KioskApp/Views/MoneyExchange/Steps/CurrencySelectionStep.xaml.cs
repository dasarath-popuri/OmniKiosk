using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using OmniKiosk.Wpf.Services.MoneyExchange;

namespace OmniKiosk.Wpf.Views.MoneyExchange.Steps
{
    public partial class CurrencySelectionStep : UserControl, IStepNav
    {
        private readonly MoneyExchangeFlowController _ctl;
        public event EventHandler? NextRequested;
        public event EventHandler? BackRequested;
        public event EventHandler? ExitRequested;

        public CurrencySelectionStep(MoneyExchangeFlowController ctl)
        {
            InitializeComponent();
            _ctl = ctl;
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            var currencies = new List<CurrencyDisplayOption>
            {
                new() { Code = "USD", FlagUrl = "https://flagcdn.com/w160/us.png", CountryName = "US Dollar", RateToMyr = 4.75 },
                new() { Code = "SGD", FlagUrl = "https://flagcdn.com/w160/sg.png", CountryName = "Singapore Dollar", RateToMyr = 3.52 },
                new() { Code = "EUR", FlagUrl = "https://flagcdn.com/w160/eu.png", CountryName = "Euro", RateToMyr = 5.12 },
                new() { Code = "GBP", FlagUrl = "https://flagcdn.com/w160/gb.png", CountryName = "British Pound", RateToMyr = 6.01 },
                new() { Code = "AUD", FlagUrl = "https://flagcdn.com/w160/au.png", CountryName = "Australian Dollar", RateToMyr = 3.10 },
                new() { Code = "JPY", FlagUrl = "https://flagcdn.com/w160/jp.png", CountryName = "Japanese Yen", RateToMyr = 0.032 },
                new() { Code = "IDR", FlagUrl = "https://flagcdn.com/w160/id.png", CountryName = "Indonesian Rupiah", RateToMyr = 0.000232 },
                new() { Code = "CNY", FlagUrl = "https://flagcdn.com/w160/cn.png", CountryName = "Chinese Yuan", RateToMyr = 0.572 }
            };
            foreach (var c in currencies) c.RateDisplay = $"1 {c.Code} = RM {c.RateToMyr:0.00}";
            LstCurrencies.ItemsSource = currencies;
        }

        private void LstCurrencies_SelectionChanged(object sender, SelectionChangedEventArgs e) => BtnNext.IsEnabled = LstCurrencies.SelectedItem != null;
        private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            if (LstCurrencies.SelectedItem is CurrencyDisplayOption selected)
            {
                _ctl.State.FromCurrency = selected.Code;
                _ctl.State.RateToMyr = selected.RateToMyr;
                NextRequested?.Invoke(this, EventArgs.Empty);
            }
        }
    }
    public class CurrencyDisplayOption { public string Code { get; set; } = ""; public string FlagUrl { get; set; } = ""; public string CountryName { get; set; } = ""; public double RateToMyr { get; set; } public string RateDisplay { get; set; } = ""; }
}