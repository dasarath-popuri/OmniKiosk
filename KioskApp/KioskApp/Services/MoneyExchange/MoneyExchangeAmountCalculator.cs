using System;

namespace OmniKiosk.Wpf.Services.MoneyExchange
{
    public static class MoneyExchangeAmountCalculator
    {
        public static decimal GetExactMyr(decimal foreignAmount, decimal rateToMyr)
        {
            if (foreignAmount <= 0 || rateToMyr <= 0)
                return 0;

            return foreignAmount * rateToMyr;
        }

        public static decimal GetPayableMyr(decimal foreignAmount, decimal rateToMyr)
        {
            var exact = GetExactMyr(foreignAmount, rateToMyr);
            return Math.Round(exact, 0, MidpointRounding.AwayFromZero);
        }
    }
}