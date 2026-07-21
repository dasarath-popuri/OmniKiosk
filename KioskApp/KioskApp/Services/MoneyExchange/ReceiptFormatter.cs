using System;
using System.Text;
using OmniKiosk.Wpf.Sdk.Printer;

namespace OmniKiosk.Wpf.Services.MoneyExchange
{
    public static class ReceiptFormatter
    {
        public const string CompanyName = "VIVO MONEY SDN BHD";
        public const string CompanySuffix = "SDN BHD";
        public const string RegistrationNo = "123456";
        public const string AddressLine1 = "JALAN PEEL, SUNWAY VELOCITY,";
        public const string AddressLine2 = "Kuala Lumpur, Malaysia";
        public const string TelNo = "xxxxxxxxxx";
        public const string FaxNo = "xxxxxxxxxx";
        public const string DefaultKioskId = "K1";

        public static string BuildHeader(string transactionType)
        {
            var r = new StringBuilder();
            r.Append(BixolonPrinterService.ESC_ALIGN_CENTER);
            r.Append(BixolonPrinterService.ESC_BOLD_ON);
            r.Append(BixolonPrinterService.ESC_DOUBLE_SIZE);
            r.Append(CompanyName + "\n");
            //r.Append(CompanySuffix + "\n");
            r.Append(BixolonPrinterService.ESC_NORMAL_SIZE);
            r.Append($"({RegistrationNo})\n");
            r.Append(BixolonPrinterService.ESC_BOLD_OFF);
            r.Append("* LICENSED MONEY SERVICES BUSINESS *\n");
            r.Append(AddressLine1 + "\n");
            r.Append(AddressLine2 + "\n");
            r.Append($"TEL: {TelNo}  FAX: {FaxNo}\n");
            r.Append("--------------------------------\n");
            r.Append(BixolonPrinterService.ESC_BOLD_ON);
            r.Append($"({transactionType})\n");
            r.Append(BixolonPrinterService.ESC_BOLD_OFF);
            r.Append($"{DateTime.Now:dd/MMM/yyyy h:mm:ss tt}\n");
            return r.ToString();
        }

        public static string BuildCustomerBlock(string receiptNo, string custName, string maskedDocumentNo)
        {
            var r = new StringBuilder();
            r.Append(BixolonPrinterService.ESC_ALIGN_CENTER);
            r.Append($"RECEIPT NO: {receiptNo}\n");
            r.Append("--------------------------------\n");
            r.Append($"NAME: {custName}\n");
            r.Append($"DOCUMENT: {maskedDocumentNo}\n");
            r.Append("--------------------------------\n");
            return r.ToString();
        }

        public static string BuildFooter(bool success)
        {
            var r = new StringBuilder();
            r.Append("--------------------------------\n");
            r.Append(BixolonPrinterService.ESC_ALIGN_CENTER);
            r.Append(BixolonPrinterService.ESC_BOLD_ON);
            if (success)
            {
                r.Append("THANK YOU\n");
                r.Append("PLEASE COME AGAIN\n");
            }
            else
            {
                r.Append("PLEASE PRESENT THIS SLIP\n");
                r.Append("AT THE COUNTER\n");
            }
            r.Append(BixolonPrinterService.ESC_BOLD_OFF);
            return r.ToString();
        }

        public static string MaskDocumentNo(string? idNo)
        {
            if (string.IsNullOrWhiteSpace(idNo) || idNo.Length <= 4) return idNo ?? "";
            return new string('X', idNo.Length - 4) + idNo.Substring(idNo.Length - 4);
        }

        public static string BuildReceiptNo(long? transactionId, string kioskId = DefaultKioskId)
        {
            return transactionId.HasValue
                ? $"OR-{kioskId}-{transactionId.Value:D8}"
                : $"OR-{kioskId}-{DateTime.Now:yyMMddHHmmss}";
        }
    }
}