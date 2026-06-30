using System;
using System.Collections.Generic;
using System.Linq;

namespace OmniKiosk.Wpf.Services.Remittance
{
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; }
    }

    public class BankInfo
    {
        public string BankName { get; set; }
        public string BankCode { get; set; }
        public string SwiftCode { get; set; }
    }

    public class TransactionCalculation
    {
        public decimal SendAmountMYR { get; set; }
        public decimal ServiceFee { get; set; }
        public decimal TotalCostMYR { get; set; }
        public decimal ExchangeRate { get; set; }
        public decimal ReceiveAmount { get; set; }
        public string ReceiveCurrency { get; set; }
    }

    public class RateService
    {
        private readonly Dictionary<string, decimal> _exchangeRates;
        private readonly Dictionary<string, string> _currencies;
        private readonly Dictionary<string, List<BankInfo>> _countryBanks;

        public RateService()
        {
            // CORRECTED EXCHANGE RATES
            // Format: 1 MYR = X Foreign Currency
            _exchangeRates = new Dictionary<string, decimal>
            {
                // Southeast Asia
                { "PH", 14.63m },    // 1 MYR = 14.63 PHP (Philippine Peso)
                { "ID", 3586.00m },  // 1 MYR = 3,586 IDR (Indonesian Rupiah)
                { "SG", 0.30m },     // 1 MYR = 0.30 SGD (Singapore Dollar)
                { "TH", 7.85m },     // 1 MYR = 7.85 THB (Thai Baht)
                { "VN", 5654.00m },  // 1 MYR = 5,654 VND (Vietnamese Dong)
                
                // South Asia
                { "IN", 18.95m },    // 1 MYR = 18.95 INR (Indian Rupee)
                { "BD", 26.85m },    // 1 MYR = 26.85 BDT (Bangladeshi Taka)
                { "PK", 62.50m },    // 1 MYR = 62.50 PKR (Pakistani Rupee)
                { "NP", 30.35m },    // 1 MYR = 30.35 NPR (Nepalese Rupee)
                { "LK", 68.45m },    // 1 MYR = 68.45 LKR (Sri Lankan Rupee)
                
                // Middle East
                { "SA", 0.85m },     // 1 MYR = 0.85 SAR (Saudi Riyal)
                { "AE", 0.83m },     // 1 MYR = 0.83 AED (UAE Dirham)
                
                // East Asia
                { "CN", 1.63m },     // 1 MYR = 1.63 CNY (Chinese Yuan)
                
                // Western Countries
                { "US", 0.226m },    // 1 MYR = 0.226 USD (US Dollar)
                { "CA", 0.32m },     // 1 MYR = 0.32 CAD (Canadian Dollar)
                { "AU", 0.35m },     // 1 MYR = 0.35 AUD (Australian Dollar)
                { "NZ", 0.38m },     // 1 MYR = 0.38 NZD (New Zealand Dollar)
                { "GB", 0.18m },     // 1 MYR = 0.18 GBP (British Pound)
                { "EU", 0.21m }      // 1 MYR = 0.21 EUR (Euro)
            };

            _currencies = new Dictionary<string, string>
            {
                { "PH", "PHP" }, { "ID", "IDR" }, { "IN", "INR" },
                { "SG", "SGD" }, { "TH", "THB" }, { "BD", "BDT" },
                { "PK", "PKR" }, { "NP", "NPR" }, { "LK", "LKR" },
                { "VN", "VND" }, { "SA", "SAR" }, { "AE", "AED" },
                { "CN", "CNY" }, { "US", "USD" }, { "CA", "CAD" },
                { "AU", "AUD" }, { "NZ", "NZD" }, { "GB", "GBP" },
                { "EU", "EUR" }
            };

            // Initialize banks by country
            _countryBanks = new Dictionary<string, List<BankInfo>>
            {
                { "PH", new List<BankInfo>
                    {
                        new BankInfo { BankName = "BDO Unibank", BankCode = "BNORPHMM", SwiftCode = "BNORPHMM" },
                        new BankInfo { BankName = "Bank of the Philippine Islands", BankCode = "BOPIPHMM", SwiftCode = "BOPIPHMM" },
                        new BankInfo { BankName = "Metrobank", BankCode = "MBTCPHMM", SwiftCode = "MBTCPHMM" },
                        new BankInfo { BankName = "Philippine National Bank", BankCode = "PNBMPHMM", SwiftCode = "PNBMPHMM" },
                        new BankInfo { BankName = "Land Bank", BankCode = "TLBPPHMM", SwiftCode = "TLBPPHMM" },
                        new BankInfo { BankName = "Union Bank", BankCode = "UBPHPHMM", SwiftCode = "UBPHPHMM" },
                        new BankInfo { BankName = "RCBC", BankCode = "RCBCPHMM", SwiftCode = "RCBCPHMM" },
                        new BankInfo { BankName = "Security Bank", BankCode = "SETCPHMM", SwiftCode = "SETCPHMM" },
                        new BankInfo { BankName = "China Bank", BankCode = "CHBKPHMM", SwiftCode = "CHBKPHMM" },
                        new BankInfo { BankName = "East West Bank", BankCode = "EWBCPHMM", SwiftCode = "EWBCPHMM" }
                    }
                },
                { "ID", new List<BankInfo>
                    {
                        new BankInfo { BankName = "Bank Mandiri", BankCode = "BMRIIDJA", SwiftCode = "BMRIIDJA" },
                        new BankInfo { BankName = "Bank Central Asia", BankCode = "CENAIDJA", SwiftCode = "CENAIDJA" },
                        new BankInfo { BankName = "Bank Negara Indonesia", BankCode = "BNINIDJA", SwiftCode = "BNINIDJA" },
                        new BankInfo { BankName = "Bank Rakyat Indonesia", BankCode = "BRINIDJA", SwiftCode = "BRINIDJA" },
                        new BankInfo { BankName = "CIMB Niaga", BankCode = "BNIAIDJA", SwiftCode = "BNIAIDJA" },
                        new BankInfo { BankName = "Bank Danamon", BankCode = "BDINIDJA", SwiftCode = "BDINIDJA" },
                        new BankInfo { BankName = "Bank Permata", BankCode = "BBBAIDJA", SwiftCode = "BBBAIDJA" },
                        new BankInfo { BankName = "Bank BTPN", BankCode = "BTPNIDJA", SwiftCode = "BTPNIDJA" }
                    }
                },
                { "IN", new List<BankInfo>
                    {
                        new BankInfo { BankName = "State Bank of India", BankCode = "SBININBB", SwiftCode = "SBININBB" },
                        new BankInfo { BankName = "HDFC Bank", BankCode = "HDFCINBB", SwiftCode = "HDFCINBB" },
                        new BankInfo { BankName = "ICICI Bank", BankCode = "ICICINBB", SwiftCode = "ICICINBB" },
                        new BankInfo { BankName = "Axis Bank", BankCode = "AXISINBB", SwiftCode = "AXISINBB" },
                        new BankInfo { BankName = "Punjab National Bank", BankCode = "PUNBINBB", SwiftCode = "PUNBINBB" },
                        new BankInfo { BankName = "Bank of Baroda", BankCode = "BARBINBB", SwiftCode = "BARBINBB" },
                        new BankInfo { BankName = "Canara Bank", BankCode = "CNRBINBB", SwiftCode = "CNRBINBB" },
                        new BankInfo { BankName = "Kotak Mahindra Bank", BankCode = "KKBKINBB", SwiftCode = "KKBKINBB" },
                        new BankInfo { BankName = "Yes Bank", BankCode = "YESBINBB", SwiftCode = "YESBINBB" },
                        new BankInfo { BankName = "IDBI Bank", BankCode = "IBKLINBB", SwiftCode = "IBKLINBB" }
                    }
                },
                { "SG", new List<BankInfo>
                    {
                        new BankInfo { BankName = "DBS Bank", BankCode = "DBSSSGSG", SwiftCode = "DBSSSGSG" },
                        new BankInfo { BankName = "OCBC Bank", BankCode = "OCBCSGSG", SwiftCode = "OCBCSGSG" },
                        new BankInfo { BankName = "UOB", BankCode = "UOVBSGSG", SwiftCode = "UOVBSGSG" },
                        new BankInfo { BankName = "Standard Chartered Bank", BankCode = "SCBLSGSG", SwiftCode = "SCBLSGSG" },
                        new BankInfo { BankName = "HSBC Singapore", BankCode = "HSBCSGSG", SwiftCode = "HSBCSGSG" },
                        new BankInfo { BankName = "Citibank Singapore", BankCode = "CITISGSG", SwiftCode = "CITISGSG" }
                    }
                },
                { "TH", new List<BankInfo>
                    {
                        new BankInfo { BankName = "Bangkok Bank", BankCode = "BKKBTHBK", SwiftCode = "BKKBTHBK" },
                        new BankInfo { BankName = "Kasikornbank", BankCode = "KASITHBK", SwiftCode = "KASITHBK" },
                        new BankInfo { BankName = "Siam Commercial Bank", BankCode = "SICOTHBK", SwiftCode = "SICOTHBK" },
                        new BankInfo { BankName = "Krung Thai Bank", BankCode = "KRTHTHBK", SwiftCode = "KRTHTHBK" },
                        new BankInfo { BankName = "TMB Bank", BankCode = "TMBKTHBK", SwiftCode = "TMBKTHBK" },
                        new BankInfo { BankName = "Bank of Ayudhya (Krungsri)", BankCode = "AYUDTHBK", SwiftCode = "AYUDTHBK" }
                    }
                },
                { "BD", new List<BankInfo>
                    {
                        new BankInfo { BankName = "Dutch-Bangla Bank", BankCode = "DBBLBDDH", SwiftCode = "DBBLBDDH" },
                        new BankInfo { BankName = "Brac Bank", BankCode = "BRACBDDH", SwiftCode = "BRACBDDH" },
                        new BankInfo { BankName = "Eastern Bank", BankCode = "EBLDBDDH", SwiftCode = "EBLDBDDH" },
                        new BankInfo { BankName = "City Bank", BankCode = "CIBLBDDH", SwiftCode = "CIBLBDDH" },
                        new BankInfo { BankName = "IFIC Bank", BankCode = "IFICBDDH", SwiftCode = "IFICBDDH" },
                        new BankInfo { BankName = "Standard Bank", BankCode = "STDBBDDH", SwiftCode = "STDBBDDH" }
                    }
                },
                { "PK", new List<BankInfo>
                    {
                        new BankInfo { BankName = "Habib Bank Limited", BankCode = "HABBPKKA", SwiftCode = "HABBPKKA" },
                        new BankInfo { BankName = "United Bank Limited", BankCode = "UNILPKKA", SwiftCode = "UNILPKKA" },
                        new BankInfo { BankName = "MCB Bank", BankCode = "MUCBPKKA", SwiftCode = "MUCBPKKA" },
                        new BankInfo { BankName = "Allied Bank Limited", BankCode = "ABPAPKKA", SwiftCode = "ABPAPKKA" },
                        new BankInfo { BankName = "National Bank of Pakistan", BankCode = "NBPAPKKA", SwiftCode = "NBPAPKKA" },
                        new BankInfo { BankName = "Bank Alfalah", BankCode = "ALFHPKKA", SwiftCode = "ALFHPKKA" }
                    }
                },
                { "NP", new List<BankInfo>
                    {
                        new BankInfo { BankName = "Nepal Bank Limited", BankCode = "NEBLNPKA", SwiftCode = "NEBLNPKA" },
                        new BankInfo { BankName = "Rastriya Banijya Bank", BankCode = "RBBANPKA", SwiftCode = "RBBANPKA" },
                        new BankInfo { BankName = "Nabil Bank", BankCode = "NARBNPKA", SwiftCode = "NARBNPKA" },
                        new BankInfo { BankName = "Nepal Investment Bank", BankCode = "NIBLNPKA", SwiftCode = "NIBLNPKA" },
                        new BankInfo { BankName = "Standard Chartered Bank Nepal", BankCode = "SCBLNPKA", SwiftCode = "SCBLNPKA" },
                        new BankInfo { BankName = "Himalayan Bank", BankCode = "HIMANPKA", SwiftCode = "HIMANPKA" }
                    }
                },
                { "LK", new List<BankInfo>
                    {
                        new BankInfo { BankName = "Bank of Ceylon", BankCode = "BCEYLKLX", SwiftCode = "BCEYLKLX" },
                        new BankInfo { BankName = "People's Bank", BankCode = "PSBKLKLX", SwiftCode = "PSBKLKLX" },
                        new BankInfo { BankName = "Commercial Bank of Ceylon", BankCode = "CCEYLKLX", SwiftCode = "CCEYLKLX" },
                        new BankInfo { BankName = "Hatton National Bank", BankCode = "HBLILKLX", SwiftCode = "HBLILKLX" },
                        new BankInfo { BankName = "Sampath Bank", BankCode = "BSAMLKLX", SwiftCode = "BSAMLKLX" },
                        new BankInfo { BankName = "National Development Bank", BankCode = "NDBSLKLC", SwiftCode = "NDBSLKLC" }
                    }
                },
                { "VN", new List<BankInfo>
                    {
                        new BankInfo { BankName = "Vietcombank", BankCode = "BFTVVNVX", SwiftCode = "BFTVVNVX" },
                        new BankInfo { BankName = "BIDV", BankCode = "BIDVVNVX", SwiftCode = "BIDVVNVX" },
                        new BankInfo { BankName = "Asia Commercial Bank", BankCode = "ASCBVNVX", SwiftCode = "ASCBVNVX" },
                        new BankInfo { BankName = "Techcombank", BankCode = "VTCBVNVX", SwiftCode = "VTCBVNVX" },
                        new BankInfo { BankName = "VietinBank", BankCode = "ICBVVNVX", SwiftCode = "ICBVVNVX" },
                        new BankInfo { BankName = "Military Bank", BankCode = "MSCBVNVX", SwiftCode = "MSCBVNVX" }
                    }
                },
                { "SA", new List<BankInfo>
                    {
                        new BankInfo { BankName = "Al Rajhi Bank", BankCode = "RJHISARI", SwiftCode = "RJHISARI" },
                        new BankInfo { BankName = "National Commercial Bank", BankCode = "NCBKSAJE", SwiftCode = "NCBKSAJE" },
                        new BankInfo { BankName = "Samba Financial Group", BankCode = "SAMBSARI", SwiftCode = "SAMBSARI" },
                        new BankInfo { BankName = "Riyad Bank", BankCode = "RIBLSARI", SwiftCode = "RIBLSARI" },
                        new BankInfo { BankName = "Banque Saudi Fransi", BankCode = "BSFRSARI", SwiftCode = "BSFRSARI" }
                    }
                },
                { "AE", new List<BankInfo>
                    {
                        new BankInfo { BankName = "Emirates NBD", BankCode = "EBILAEAD", SwiftCode = "EBILAEAD" },
                        new BankInfo { BankName = "Abu Dhabi Commercial Bank", BankCode = "ADCBAEAD", SwiftCode = "ADCBAEAD" },
                        new BankInfo { BankName = "First Abu Dhabi Bank", BankCode = "NBADAEAD", SwiftCode = "NBADAEAD" },
                        new BankInfo { BankName = "Dubai Islamic Bank", BankCode = "DUIBAEAD", SwiftCode = "DUIBAEAD" },
                        new BankInfo { BankName = "Mashreq Bank", BankCode = "BOMLAEAD", SwiftCode = "BOMLAEAD" },
                        new BankInfo { BankName = "RAKBank", BankCode = "NRAKAEAK", SwiftCode = "NRAKAEAK" }
                    }
                },
                { "CN", new List<BankInfo>
                    {
                        new BankInfo { BankName = "ICBC", BankCode = "ICBKCNBJ", SwiftCode = "ICBKCNBJ" },
                        new BankInfo { BankName = "China Construction Bank", BankCode = "PCBCCNBJ", SwiftCode = "PCBCCNBJ" },
                        new BankInfo { BankName = "Agricultural Bank of China", BankCode = "ABOCCNBJ", SwiftCode = "ABOCCNBJ" },
                        new BankInfo { BankName = "Bank of China", BankCode = "BKCHCNBJ", SwiftCode = "BKCHCNBJ" },
                        new BankInfo { BankName = "China Merchants Bank", BankCode = "CMBCCNBS", SwiftCode = "CMBCCNBS" },
                        new BankInfo { BankName = "Postal Savings Bank of China", BankCode = "PSBC", SwiftCode = "PSBCCNBJ" }
                    }
                },
                { "US", new List<BankInfo>
                    {
                        new BankInfo { BankName = "Bank of America", BankCode = "BOFAUS3N", SwiftCode = "BOFAUS3N" },
                        new BankInfo { BankName = "JPMorgan Chase", BankCode = "CHASUS33", SwiftCode = "CHASUS33" },
                        new BankInfo { BankName = "Wells Fargo", BankCode = "WFBIUS6S", SwiftCode = "WFBIUS6S" },
                        new BankInfo { BankName = "Citibank", BankCode = "CITIUS33", SwiftCode = "CITIUS33" },
                        new BankInfo { BankName = "U.S. Bank", BankCode = "USBKUS44", SwiftCode = "USBKUS44" },
                        new BankInfo { BankName = "PNC Bank", BankCode = "PNCCUS33", SwiftCode = "PNCCUS33" }
                    }
                },
                { "CA", new List<BankInfo>
                    {
                        new BankInfo { BankName = "Royal Bank of Canada", BankCode = "ROYCCAT2", SwiftCode = "ROYCCAT2" },
                        new BankInfo { BankName = "TD Canada Trust", BankCode = "TDOMCATTTOR", SwiftCode = "TDOMCATTTOR" },
                        new BankInfo { BankName = "Scotiabank", BankCode = "NOSCCATT", SwiftCode = "NOSCCATT" },
                        new BankInfo { BankName = "Bank of Montreal", BankCode = "BOFMCAM2", SwiftCode = "BOFMCAM2" },
                        new BankInfo { BankName = "CIBC", BankCode = "CIBCCATT", SwiftCode = "CIBCCATT" },
                        new BankInfo { BankName = "National Bank of Canada", BankCode = "BNDCCAMMINT", SwiftCode = "BNDCCAMMINT" }
                    }
                },
                { "AU", new List<BankInfo>
                    {
                        new BankInfo { BankName = "Commonwealth Bank", BankCode = "CTBAAU2S", SwiftCode = "CTBAAU2S" },
                        new BankInfo { BankName = "Westpac", BankCode = "WPACAU2S", SwiftCode = "WPACAU2S" },
                        new BankInfo { BankName = "ANZ Bank", BankCode = "ANZBAU3M", SwiftCode = "ANZBAU3M" },
                        new BankInfo { BankName = "National Australia Bank", BankCode = "NATAAU3303M", SwiftCode = "NATAAU3303M" }
                    }
                },
                { "NZ", new List<BankInfo>
                    {
                        new BankInfo { BankName = "ANZ Bank New Zealand", BankCode = "ANZBNZ22", SwiftCode = "ANZBNZ22" },
                        new BankInfo { BankName = "ASB Bank", BankCode = "ASBBNZ2A", SwiftCode = "ASBBNZ2A" },
                        new BankInfo { BankName = "Bank of New Zealand", BankCode = "BKNZNZ22", SwiftCode = "BKNZNZ22" },
                        new BankInfo { BankName = "Westpac New Zealand", BankCode = "WPACNZ2W", SwiftCode = "WPACNZ2W" }
                    }
                },
                { "GB", new List<BankInfo>
                    {
                        new BankInfo { BankName = "HSBC UK", BankCode = "HBUKGB4B", SwiftCode = "HBUKGB4B" },
                        new BankInfo { BankName = "Lloyds Bank", BankCode = "LOYDGB2L", SwiftCode = "LOYDGB2L" },
                        new BankInfo { BankName = "Barclays", BankCode = "BARCGB22", SwiftCode = "BARCGB22" },
                        new BankInfo { BankName = "NatWest", BankCode = "NWBKGB2L", SwiftCode = "NWBKGB2L" },
                        new BankInfo { BankName = "Santander UK", BankCode = "ABBYGB2L", SwiftCode = "ABBYGB2L" }
                    }
                },
                { "EU", new List<BankInfo>
                    {
                        new BankInfo { BankName = "Deutsche Bank", BankCode = "DEUTDEFF", SwiftCode = "DEUTDEFF" },
                        new BankInfo { BankName = "BNP Paribas", BankCode = "BNPAFRPP", SwiftCode = "BNPAFRPP" },
                        new BankInfo { BankName = "Crédit Agricole", BankCode = "AGRIFRPP", SwiftCode = "AGRIFRPP" },
                        new BankInfo { BankName = "ING Bank", BankCode = "INGBNL2A", SwiftCode = "INGBNL2A" },
                        new BankInfo { BankName = "UniCredit", BankCode = "UNCRITM1", SwiftCode = "UNCRITM1" },
                        new BankInfo { BankName = "Banco Santander", BankCode = "BSCHESMM", SwiftCode = "BSCHESMM" }
                    }
                }
            };

            // Add default banks for any country not explicitly defined
            foreach (var countryCode in _currencies.Keys)
            {
                if (!_countryBanks.ContainsKey(countryCode))
                {
                    _countryBanks[countryCode] = new List<BankInfo>
                    {
                        new BankInfo { BankName = "National Bank", BankCode = "NAT001", SwiftCode = "NAT001" },
                        new BankInfo { BankName = "Commercial Bank", BankCode = "COM001", SwiftCode = "COM001" },
                        new BankInfo { BankName = "Central Bank", BankCode = "CEN001", SwiftCode = "CEN001" }
                    };
                }
            }
        }

        public TransactionCalculation CalculateTransaction(string countryCode, decimal sendAmountMYR)
        {
            if (!_exchangeRates.ContainsKey(countryCode))
            {
                throw new ArgumentException("Invalid country code");
            }

            decimal rate = _exchangeRates[countryCode];
            decimal serviceFee = CalculateServiceFee(sendAmountMYR);
            decimal totalCost = sendAmountMYR + serviceFee;

            // CORRECTED FORMULA: Multiply by rate instead of dividing
            // Formula: Send Amount (MYR) × Exchange Rate = Receive Amount (Foreign Currency)
            decimal receiveAmount = sendAmountMYR * rate;

            return new TransactionCalculation
            {
                SendAmountMYR = sendAmountMYR,
                ServiceFee = serviceFee,
                TotalCostMYR = totalCost,
                ExchangeRate = rate,
                ReceiveAmount = receiveAmount,
                ReceiveCurrency = _currencies[countryCode]
            };
        }

        public ValidationResult ValidateTransactionAmount(decimal amount)
        {
            if (amount <= 0)
            {
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Amount must be greater than zero"
                };
            }

            if (amount < 50)
            {
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Minimum transaction amount is 50.00 MYR"
                };
            }

            if (amount > 30000)
            {
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Maximum transaction amount is 30,000.00 MYR"
                };
            }

            return new ValidationResult { IsValid = true };
        }

        public List<BankInfo> GetBanksForCountry(string countryCode)
        {
            if (_countryBanks.ContainsKey(countryCode))
            {
                return _countryBanks[countryCode];
            }

            return new List<BankInfo>
            {
                new BankInfo { BankName = "National Bank", BankCode = "NAT001", SwiftCode = "NAT001" },
                new BankInfo { BankName = "Commercial Bank", BankCode = "COM001", SwiftCode = "COM001" }
            };
        }

        private decimal CalculateServiceFee(decimal amount)
        {
            // Tiered service fee structure
            if (amount < 500)
                return 10m;
            else if (amount < 1000)
                return 15m;
            else if (amount < 5000)
                return 20m;
            else if (amount < 10000)
                return 25m;
            else
                return 30m;
        }

        public decimal GetExchangeRate(string countryCode)
        {
            return _exchangeRates.ContainsKey(countryCode) ? _exchangeRates[countryCode] : 0;
        }

        public string GetCurrency(string countryCode)
        {
            return _currencies.ContainsKey(countryCode) ? _currencies[countryCode] : "";
        }

        public string FormatExchangeRate(decimal rate)
        {
            // Convert to string with maximum 4 decimals
            string formatted = rate.ToString("0.####");

            // If no decimal point, add .00
            if (!formatted.Contains("."))
            {
                return formatted + ".00";
            }

            // Get decimal part
            string[] parts = formatted.Split('.');
            string decimalPart = parts[1];

            // Ensure minimum 2 decimal places
            if (decimalPart.Length == 1)
            {
                decimalPart += "0";
            }

            return $"{parts[0]}.{decimalPart}";
        }
    }
}