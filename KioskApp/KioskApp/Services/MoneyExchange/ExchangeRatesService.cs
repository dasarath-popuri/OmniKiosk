using System;
using System.Collections.Generic;

namespace OmniKiosk.Wpf.Services.MoneyExchange
{
    public sealed class ExchangeRatesService
    {
        // TODO: replace with DB / API later
        private readonly Dictionary<string, double> _toMyr = new(StringComparer.OrdinalIgnoreCase)
        {
            // 1 unit foreign -> MYR
            ["USD"] = 4.70,
            ["SGD"] = 3.50,
            ["EUR"] = 5.10,
            ["INR"] = 0.056,

            // Added missing currencies to match the UI grid
            ["CHF"] = 5.30,
            ["CNY"] = 0.65,
            ["GBP"] = 5.95,
            ["IDR"] = 0.0003,
            ["JPY"] = 0.031,
            ["KRW"] = 0.0035
        };

        public IReadOnlyList<string> SupportedCurrencies => new List<string>(_toMyr.Keys);

        public double GetRateToMyr(string currency)
        {
            if (!_toMyr.TryGetValue(currency, out var r))
                throw new InvalidOperationException("Unsupported currency: " + currency);
            return r;
        }

        public double CalcMyr(string currency, double foreignAmount)
        {
            var rate = GetRateToMyr(currency);
            return Math.Round(foreignAmount * rate, 2);
        }
    }
}