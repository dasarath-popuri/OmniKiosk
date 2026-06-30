//using System;

//namespace OmniKiosk.Wpf.Models
//{
//    public class BeneficiaryModel
//    {
//        public int Id { get; set; }
//        public string FullName { get; set; }
//        public string Country { get; set; }
//        public string CountryCode { get; set; }
//        public string MobileNo { get; set; }
//        public string Address { get; set; }
//        public string City { get; set; }
//        public string BankName { get; set; }
//        public string BankCode { get; set; }
//        public string AccountNo { get; set; }
//        public string Relationship { get; set; }
//        public DateTime CreatedDate { get; set; }
//        public DateTime LastUsedDate { get; set; }
//        public int UsageCount { get; set; }

//        public string DisplayName => $"{FullName} - {Country}";
//        public string AccountDetails => $"{BankName} - {AccountNo}";
//        public string LastUsedText => $"Last used: {LastUsedDate:dd/MM/yyyy} ({UsageCount} times)";

//        // Additional BNM required fields
//        public string IFSC { get; set; }
//        public string RoutingNumber { get; set; }
//        public string SwiftCode { get; set; }
//        public string IBAN { get; set; }
//        public string CustomerIdNo { get; set; }
//    }
//}
using System;

namespace OmniKiosk.Wpf.Models
{
    public class BeneficiaryModel
    {
        public int Id { get; set; }
        public string CustomerIdNo { get; set; } // NEW: Link beneficiary to customer
        public string FullName { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string MobileNo { get; set; }
        public string Nationality { get; set; }
        public string Relationship { get; set; }
        public string Country { get; set; }
        public string CountryCode { get; set; }
        public string BankName { get; set; }
        public string BankCode { get; set; }
        public string AccountNo { get; set; }
        public string Address { get; set; }
        public string City { get; set; }

        // BNM Required Fields
        public string IFSC { get; set; }
        public string RoutingNumber { get; set; }
        public string SwiftCode { get; set; }
        public string IBAN { get; set; }

        public DateTime CreatedDate { get; set; }
        public DateTime LastUsedDate { get; set; }
        public int UsageCount { get; set; }

        public string LastUsedText => LastUsedDate == default(DateTime)
            ? "Never used"
            : $"Last used: {LastUsedDate:dd MMM yyyy}";
    }
}