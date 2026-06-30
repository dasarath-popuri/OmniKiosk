using System;

namespace OmniKiosk.Config.Api.Models.v1
{
    public class ExchangeRate
    {
        public string CurrencyCode { get; set; }
        public double RateToMyr { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}