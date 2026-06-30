using System.Collections.Generic;

namespace OmniKiosk.Wpf.Models
{
    public class BeneficiaryFieldRequirement
    {
        public string FieldName { get; set; }
        public string Label { get; set; }
        public string Placeholder { get; set; }
        public bool IsRequired { get; set; }
    }

    public static class BeneficiaryFieldRequirements
    {
        public static List<BeneficiaryFieldRequirement> GetRequiredFields(string countryCode, string deliveryMethod)
        {
            var fields = new List<BeneficiaryFieldRequirement>();

            // India - Bank Account Credit
            if (countryCode == "IN" && deliveryMethod?.Contains("Bank") == true)
            {
                fields.Add(new BeneficiaryFieldRequirement
                {
                    FieldName = "IFSC",
                    Label = "IFSC Code *",
                    Placeholder = "Enter 11-digit IFSC Code (e.g., SBIN0001234)",
                    IsRequired = true
                });
            }

            // Bangladesh - Bank Account Credit
            if (countryCode == "BD" && deliveryMethod?.Contains("Bank") == true)
            {
                fields.Add(new BeneficiaryFieldRequirement
                {
                    FieldName = "RoutingNumber",
                    Label = "Routing Number *",
                    Placeholder = "Enter 9-digit Routing Number",
                    IsRequired = true
                });
            }

            // Philippines - Bank Account Credit
            if (countryCode == "PH" && deliveryMethod?.Contains("Bank") == true)
            {
                fields.Add(new BeneficiaryFieldRequirement
                {
                    FieldName = "SwiftCode",
                    Label = "SWIFT/BIC Code *",
                    Placeholder = "Enter SWIFT Code (e.g., BOPIPHMM)",
                    IsRequired = true
                });
            }

            // Pakistan - Bank Account Credit
            if (countryCode == "PK" && deliveryMethod?.Contains("Bank") == true)
            {
                fields.Add(new BeneficiaryFieldRequirement
                {
                    FieldName = "IBAN",
                    Label = "IBAN *",
                    Placeholder = "Enter IBAN (e.g., PK36SCBL0000001123456702)",
                    IsRequired = true
                });
            }

            return fields;
        }
    }
}