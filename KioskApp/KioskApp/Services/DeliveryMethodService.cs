using System.Collections.Generic;
using System.Linq;

namespace OmniKiosk.Wpf.Services.Remittance
{
    public class DeliveryMethodService
    {
        private readonly Dictionary<string, List<DeliveryMethodInfo>> _deliveryMethods;

        public DeliveryMethodService()
        {
            _deliveryMethods = InitializeDeliveryMethods();
        }

        private Dictionary<string, List<DeliveryMethodInfo>> InitializeDeliveryMethods()
        {
            return new Dictionary<string, List<DeliveryMethodInfo>>
            {
                // Philippines
                ["PH"] = new List<DeliveryMethodInfo>
                {
                    new DeliveryMethodInfo { Code = "BANK", Name = "Bank Account Credit", Icon = "🏦" },
                    new DeliveryMethodInfo { Code = "CASH", Name = "Cash Pickup", Icon = "💵" },
                    new DeliveryMethodInfo { Code = "WALLET", Name = "Mobile Wallet (GCash, PayMaya)", Icon = "📱" },
                    new DeliveryMethodInfo { Code = "HOME", Name = "Home Delivery", Icon = "🏠" }
                },

                // Indonesia
                ["ID"] = new List<DeliveryMethodInfo>
                {
                    new DeliveryMethodInfo { Code = "BANK", Name = "Bank Account Credit", Icon = "🏦" },
                    new DeliveryMethodInfo { Code = "CASH", Name = "Cash Pickup", Icon = "💵" },
                    new DeliveryMethodInfo { Code = "WALLET", Name = "Mobile Wallet (GoPay, OVO, DANA)", Icon = "📱" },
                    new DeliveryMethodInfo { Code = "HOME", Name = "Home Delivery", Icon = "🏠" }
                },

                // India
                ["IN"] = new List<DeliveryMethodInfo>
                {
                    new DeliveryMethodInfo { Code = "BANK", Name = "Bank Account Credit (NEFT/RTGS)", Icon = "🏦" },
                    new DeliveryMethodInfo { Code = "UPI", Name = "UPI Transfer", Icon = "📲" },
                    new DeliveryMethodInfo { Code = "IMPS", Name = "IMPS (Instant Transfer)", Icon = "⚡" },
                    new DeliveryMethodInfo { Code = "CASH", Name = "Cash Pickup", Icon = "💵" },
                    new DeliveryMethodInfo { Code = "WALLET", Name = "Mobile Wallet (Paytm, PhonePe)", Icon = "📱" }
                },

                // Singapore
                ["SG"] = new List<DeliveryMethodInfo>
                {
                    new DeliveryMethodInfo { Code = "BANK", Name = "Bank Account Credit", Icon = "🏦" },
                    new DeliveryMethodInfo { Code = "PAYNOW", Name = "PayNow Transfer", Icon = "💳" },
                    new DeliveryMethodInfo { Code = "CASH", Name = "Cash Pickup", Icon = "💵" }
                },

                // Thailand
                ["TH"] = new List<DeliveryMethodInfo>
                {
                    new DeliveryMethodInfo { Code = "BANK", Name = "Bank Account Credit", Icon = "🏦" },
                    new DeliveryMethodInfo { Code = "CASH", Name = "Cash Pickup", Icon = "💵" },
                    new DeliveryMethodInfo { Code = "WALLET", Name = "Mobile Wallet (TrueMoney, Rabbit LINE Pay)", Icon = "📱" },
                    new DeliveryMethodInfo { Code = "PROMPTPAY", Name = "PromptPay", Icon = "💳" }
                },

                // Bangladesh
                ["BD"] = new List<DeliveryMethodInfo>
                {
                    new DeliveryMethodInfo { Code = "BANK", Name = "Bank Account Credit", Icon = "🏦" },
                    new DeliveryMethodInfo { Code = "CASH", Name = "Cash Pickup", Icon = "💵" },
                    new DeliveryMethodInfo { Code = "WALLET", Name = "Mobile Wallet (bKash, Nagad, Rocket)", Icon = "📱" },
                    new DeliveryMethodInfo { Code = "HOME", Name = "Home Delivery", Icon = "🏠" }
                },

                // Pakistan
                ["PK"] = new List<DeliveryMethodInfo>
                {
                    new DeliveryMethodInfo { Code = "BANK", Name = "Bank Account Credit", Icon = "🏦" },
                    new DeliveryMethodInfo { Code = "CASH", Name = "Cash Pickup", Icon = "💵" },
                    new DeliveryMethodInfo { Code = "WALLET", Name = "Mobile Wallet (EasyPaisa, JazzCash)", Icon = "📱" },
                    new DeliveryMethodInfo { Code = "HOME", Name = "Home Delivery", Icon = "🏠" }
                },

                // Nepal
                ["NP"] = new List<DeliveryMethodInfo>
                {
                    new DeliveryMethodInfo { Code = "BANK", Name = "Bank Account Credit", Icon = "🏦" },
                    new DeliveryMethodInfo { Code = "CASH", Name = "Cash Pickup", Icon = "💵" },
                    new DeliveryMethodInfo { Code = "WALLET", Name = "Mobile Wallet (eSewa, Khalti)", Icon = "📱" },
                    new DeliveryMethodInfo { Code = "HOME", Name = "Home Delivery", Icon = "🏠" }
                },

                // Sri Lanka
                ["LK"] = new List<DeliveryMethodInfo>
                {
                    new DeliveryMethodInfo { Code = "BANK", Name = "Bank Account Credit", Icon = "🏦" },
                    new DeliveryMethodInfo { Code = "CASH", Name = "Cash Pickup", Icon = "💵" },
                    new DeliveryMethodInfo { Code = "WALLET", Name = "Mobile Wallet", Icon = "📱" },
                    new DeliveryMethodInfo { Code = "HOME", Name = "Home Delivery", Icon = "🏠" }
                },

                // Vietnam
                ["VN"] = new List<DeliveryMethodInfo>
                {
                    new DeliveryMethodInfo { Code = "BANK", Name = "Bank Account Credit", Icon = "🏦" },
                    new DeliveryMethodInfo { Code = "CASH", Name = "Cash Pickup", Icon = "💵" },
                    new DeliveryMethodInfo { Code = "WALLET", Name = "Mobile Wallet (MoMo, ZaloPay)", Icon = "📱" },
                    new DeliveryMethodInfo { Code = "HOME", Name = "Home Delivery", Icon = "🏠" }
                },

                // Saudi Arabia
                ["SA"] = new List<DeliveryMethodInfo>
                {
                    new DeliveryMethodInfo { Code = "BANK", Name = "Bank Account Credit", Icon = "🏦" },
                    new DeliveryMethodInfo { Code = "CASH", Name = "Cash Pickup", Icon = "💵" }
                },

                // UAE
                ["AE"] = new List<DeliveryMethodInfo>
                {
                    new DeliveryMethodInfo { Code = "BANK", Name = "Bank Account Credit", Icon = "🏦" },
                    new DeliveryMethodInfo { Code = "CASH", Name = "Cash Pickup", Icon = "💵" },
                    new DeliveryMethodInfo { Code = "WALLET", Name = "Mobile Wallet", Icon = "📱" }
                },

                // China
                ["CN"] = new List<DeliveryMethodInfo>
                {
                    new DeliveryMethodInfo { Code = "BANK", Name = "Bank Account Credit (UnionPay)", Icon = "🏦" },
                    new DeliveryMethodInfo { Code = "ALIPAY", Name = "Alipay Transfer", Icon = "💳" },
                    new DeliveryMethodInfo { Code = "WECHAT", Name = "WeChat Pay", Icon = "💬" },
                    new DeliveryMethodInfo { Code = "CASH", Name = "Cash Pickup", Icon = "💵" }
                },

                // United States
                ["US"] = new List<DeliveryMethodInfo>
                {
                    new DeliveryMethodInfo { Code = "BANK", Name = "Bank Account Credit (ACH)", Icon = "🏦" },
                    new DeliveryMethodInfo { Code = "WIRE", Name = "Wire Transfer", Icon = "⚡" },
                    new DeliveryMethodInfo { Code = "CASH", Name = "Cash Pickup", Icon = "💵" },
                    new DeliveryMethodInfo { Code = "WALLET", Name = "Mobile Wallet (Venmo, CashApp)", Icon = "📱" }
                },

                // Canada
                ["CA"] = new List<DeliveryMethodInfo>
                {
                    new DeliveryMethodInfo { Code = "BANK", Name = "Bank Account Credit (EFT)", Icon = "🏦" },
                    new DeliveryMethodInfo { Code = "INTERAC", Name = "Interac e-Transfer", Icon = "💳" },
                    new DeliveryMethodInfo { Code = "CASH", Name = "Cash Pickup", Icon = "💵" }
                },

                // Australia
                ["AU"] = new List<DeliveryMethodInfo>
                {
                    new DeliveryMethodInfo { Code = "BANK", Name = "Bank Account Credit", Icon = "🏦" },
                    new DeliveryMethodInfo { Code = "OSKO", Name = "Osko (PayID)", Icon = "⚡" },
                    new DeliveryMethodInfo { Code = "CASH", Name = "Cash Pickup", Icon = "💵" }
                },

                // New Zealand
                ["NZ"] = new List<DeliveryMethodInfo>
                {
                    new DeliveryMethodInfo { Code = "BANK", Name = "Bank Account Credit", Icon = "🏦" },
                    new DeliveryMethodInfo { Code = "CASH", Name = "Cash Pickup", Icon = "💵" }
                },

                // United Kingdom
                ["GB"] = new List<DeliveryMethodInfo>
                {
                    new DeliveryMethodInfo { Code = "BANK", Name = "Bank Account Credit (Faster Payments)", Icon = "🏦" },
                    new DeliveryMethodInfo { Code = "BACS", Name = "BACS Transfer", Icon = "💳" },
                    new DeliveryMethodInfo { Code = "CASH", Name = "Cash Pickup", Icon = "💵" }
                },

                // Eurozone
                ["EU"] = new List<DeliveryMethodInfo>
                {
                    new DeliveryMethodInfo { Code = "BANK", Name = "Bank Account Credit (SEPA)", Icon = "🏦" },
                    new DeliveryMethodInfo { Code = "INSTANT", Name = "SEPA Instant Transfer", Icon = "⚡" },
                    new DeliveryMethodInfo { Code = "CASH", Name = "Cash Pickup", Icon = "💵" }
                }
            };
        }

        public List<DeliveryMethodInfo> GetDeliveryMethodsForCountry(string countryCode)
        {
            if (string.IsNullOrEmpty(countryCode))
                return new List<DeliveryMethodInfo>();

            if (_deliveryMethods.TryGetValue(countryCode, out var methods))
                return methods;

            // Default delivery methods if country not found
            return new List<DeliveryMethodInfo>
            {
                new DeliveryMethodInfo { Code = "BANK", Name = "Bank Account Credit", Icon = "🏦" },
                new DeliveryMethodInfo { Code = "CASH", Name = "Cash Pickup", Icon = "💵" }
            };
        }

        public DeliveryMethodInfo GetDeliveryMethodByCode(string countryCode, string methodCode)
        {
            var methods = GetDeliveryMethodsForCountry(countryCode);
            return methods.FirstOrDefault(m => m.Code == methodCode);
        }
    }

    public class DeliveryMethodInfo
    {
        public string Code { get; set; }
        public string Name { get; set; }
        public string Icon { get; set; }
    }
}