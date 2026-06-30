using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OmniKiosk.Wpf.Services.MoneyExchange;

namespace OmniKiosk.Wpf.Views.MoneyExchange.Steps
{
    public partial class ExchangeQuoteStep : UserControl, IStepNav
    {
        private readonly MoneyExchangeFlowController _ctl;

        public event EventHandler? NextRequested;
        public event EventHandler? BackRequested;
        public event EventHandler? ExitRequested;

        private static readonly Regex _numRx = new(@"^[0-9]*(\.[0-9]{0,2})?$");

        private bool _isBuyTransaction = true;
        private CurrencyOption? _selectedCurrency;

        public ExchangeQuoteStep(MoneyExchangeFlowController ctl)
        {
            InitializeComponent();
            _ctl = ctl;

            // Persist the transaction list globally
            if (_ctl.State.Transactions == null) _ctl.State.Transactions = new ObservableCollection<TransactionItem>();
            DgTransactions.ItemsSource = _ctl.State.Transactions;
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            var currencies = new List<CurrencyOption>
            {
                new() { Code = "CHF", Flag = "🇨🇭" },
                new() { Code = "CNY", Flag = "🇨🇳" },
                new() { Code = "EUR", Flag = "🇪🇺" },
                new() { Code = "GBP", Flag = "🇬🇧" },
                new() { Code = "IDR", Flag = "🇮🇩" },
                new() { Code = "INR", Flag = "🇮🇳" },
                new() { Code = "JPY", Flag = "🇯🇵" },
                new() { Code = "KRW", Flag = "🇰🇷" },
                new() { Code = "SGD", Flag = "🇸🇬" },
                new() { Code = "USD", Flag = "🇺🇸" }
            };

            LstCurrencies.ItemsSource = currencies;
            LstCurrencies.SelectedIndex = 0;

            UpdateUIState();
            UpdateCartTotals(); // Load persisted totals
        }

        private void BtnBuyType_MouseDown(object sender, MouseButtonEventArgs e) { _isBuyTransaction = true; UpdateUIState(); }
        private void BtnSellType_MouseDown(object sender, MouseButtonEventArgs e) { _isBuyTransaction = false; UpdateUIState(); }

        private void UpdateUIState()
        {
            if (_isBuyTransaction)
            {
                BtnBuyType.BorderBrush = (Brush)FindResource("PrimaryBrush");
                BtnBuyType.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F0F9FF"));
                BtnSellType.BorderBrush = (Brush)FindResource("BorderBrush");
                BtnSellType.Background = (Brush)FindResource("CardBrush");
            }
            else
            {
                BtnSellType.BorderBrush = (Brush)FindResource("PrimaryBrush");
                BtnSellType.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F0F9FF"));
                BtnBuyType.BorderBrush = (Brush)FindResource("BorderBrush");
                BtnBuyType.Background = (Brush)FindResource("CardBrush");
            }
            RecalcLocalAmount();
        }

        private void LstCurrencies_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstCurrencies.SelectedItem is CurrencyOption selected)
            {
                _selectedCurrency = selected;
                _ctl.SetQuote(selected.Code, 1.0);
                double currentRate = _ctl.State.RateToMyr;
                TxtLiveRate.Text = $"1 {selected.Code} = {currentRate:0.####} MYR";
                RecalcLocalAmount();
            }
        }

        private void TxtAmount_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            var next = (TxtForeignAmount.Text ?? "") + e.Text;
            e.Handled = !_numRx.IsMatch(next);
        }

        private void TxtAmount_TextChanged(object sender, TextChangedEventArgs e) => RecalcLocalAmount();

        private void RecalcLocalAmount()
        {
            if (_selectedCurrency == null) return;
            if (double.TryParse(TxtForeignAmount.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var fAmt) && fAmt > 0)
            {
                _ctl.SetQuote(_selectedCurrency.Code, fAmt);
                TxtLocalAmount.Text = $"{_ctl.State.MyrAmount:0.00}";
                BtnAddTransaction.IsEnabled = true;
            }
            else
            {
                TxtLocalAmount.Text = "0.00";
                BtnAddTransaction.IsEnabled = false;
            }
        }

        private void BtnAddTransaction_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedCurrency == null) return;
            if (double.TryParse(TxtForeignAmount.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var fAmt) && fAmt > 0)
            {
                _ctl.SetQuote(_selectedCurrency.Code, fAmt);
                _ctl.State.Transactions.Add(new TransactionItem
                {
                    Type = _isBuyTransaction ? "Buy" : "Sell",
                    CurrencyCode = _selectedCurrency.Code,
                    ForeignAmount = fAmt,
                    Rate = _ctl.State.RateToMyr,
                    MyrAmount = _ctl.State.MyrAmount
                });

                TxtForeignAmount.Text = "";
                TxtLocalAmount.Text = "0.00";
                UpdateCartTotals();
            }
        }

        private void DeleteTransaction_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is TransactionItem item)
            {
                _ctl.State.Transactions.Remove(item);
                UpdateCartTotals();
            }
        }

        private void UpdateCartTotals()
        {
            TxtItemsCount.Text = $"{_ctl.State.Transactions.Count} items";
            double totalMyr = _ctl.State.Transactions.Sum(t => t.MyrAmount);
            TxtTotalMyr.Text = $"RM {totalMyr:0.00}";
            BtnNext.IsEnabled = _ctl.State.Transactions.Count > 0;
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            if (_ctl.State.Transactions.Count == 0) return;

            _ctl.State.MyrAmount = _ctl.State.Transactions.Sum(t => t.MyrAmount);
            _ctl.State.FromCurrency = _ctl.State.Transactions.Count == 1 ? _ctl.State.Transactions[0].CurrencyCode : "MIXED";
            _ctl.State.FromAmount = _ctl.State.Transactions.Count == 1 ? _ctl.State.Transactions[0].ForeignAmount : 0;

            NextRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public class CurrencyOption
    {
        public string Code { get; set; } = string.Empty;
        public string Flag { get; set; } = string.Empty;
    }

    public class TransactionItem
    {
        public string Type { get; set; } = string.Empty;
        public string CurrencyCode { get; set; } = string.Empty;
        public double ForeignAmount { get; set; }
        public double Rate { get; set; }
        public double MyrAmount { get; set; }
    }
}