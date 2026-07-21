using System;
using OmniKiosk.Wpf.Models.MoneyExchange;
using OmniKiosk.Wpf.Services.MoneyExchange.Repos;

namespace OmniKiosk.Wpf.Services.MoneyExchange
{
    public sealed class MoneyExchangeFlowController
    {
        private readonly ExchangeRatesService _rates;
        private readonly CustomerRepository _customers;
        private readonly TransactionRepository _txns;

        public MoneyExchangeFlowState State { get; } = new MoneyExchangeFlowState();

        public MoneyExchangeFlowController()
        {
            var db = new KioskDb();
            _rates = new ExchangeRatesService();
            _customers = new CustomerRepository(db);
            _txns = new TransactionRepository(db);
        }

        public ExchangeRatesService Rates => _rates;

        public void SetQuote(string fromCurrency, double fromAmount)
        {
            State.FromCurrency = fromCurrency;
            State.FromAmount = fromAmount;
            State.RateToMyr = _rates.GetRateToMyr(fromCurrency);
            State.MyrAmount = _rates.CalcMyr(fromCurrency, fromAmount);
        }

        public CustomerProfile UpsertCustomer(CustomerProfile c)
        {
            var existing = _customers.GetByIdNo(c.IdType, c.IdNo);

            // This lookup is the single source of truth for "have we seen this
            // person before" - FaceVerificationStep reads it to decide between
            // a fast local face match and a full Innov8tif eKYC verification.
            State.IsExistingCustomer = existing != null;

            if (existing != null)
            {
                // keep existing face if new doesn't have it
                if (string.IsNullOrWhiteSpace(c.FaceFeatureBase64))
                    c.FaceFeatureBase64 = existing.FaceFeatureBase64;
                if (string.IsNullOrWhiteSpace(c.FaceImageBase64))
                    c.FaceImageBase64 = existing.FaceImageBase64;
            }

            c.LastSeenUtc = DateTime.UtcNow;
            State.Customer = _customers.Upsert(c);
            return State.Customer;
        }

        public void SaveFace(string featureB64, string faceImageB64)
        {
            if (State.Customer == null) throw new InvalidOperationException("Customer not set");
            State.Customer.FaceFeatureBase64 = featureB64;
            State.Customer.FaceImageBase64 = faceImageB64;
            UpsertCustomer(State.Customer);
        }

        public long CreateTransaction()
        {
            if (State.Customer == null) throw new InvalidOperationException("Customer not set");

            var t = new MoneyExchangeTxn
            {
                CustomerId = State.Customer.Id,
                FromCurrency = State.FromCurrency,
                FromAmount = State.FromAmount,
                Rate = State.RateToMyr,
                MyrAmount = State.MyrAmount,
                CashInsertedMyr = State.CashInsertedMyr,
                Status = "PaidIn"
            };
            var id = _txns.Insert(t);
            State.TransactionId = id;
            return id;
        }
    }
}
