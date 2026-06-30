using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OmniKiosk.Wpf.Services.Fiuu
{
    public class LoginResponse
    {
        public string accessToken { get; set; }
        public string refreshToken { get; set; }
        public string expireAt { get; set; }
    }

    public class SaleResponse
    {
        public string statusCode { get; set; }
        public string responseCode { get; set; }
        public string responseText { get; set; }
        public string signature { get; set; }
        public SaleResponseData transData { get; set; }
    }

    public class SaleResponseData
    {
        public string transId { get; set; }
        public string transAmt { get; set; }
        public string paymentChannel { get; set; }
        public string invoiceNo { get; set; }
        public string tid { get; set; }
        public string mid { get; set; }
    }
}
