using System;
using static BTCPayServer.RockstarDev.Plugins.Payroll.Controllers.PayrollInvoiceController;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Helper
{
    internal class PayrollInvoicePaymentHelper
    {
        private const decimal SatoshisPerBitcoin = 100_000_000m;
        public string PaymentInvoiceQrUrl(PayrollInvoiceViewModel model)
        {
            if (model.Currency.Equals("BTC", StringComparison.InvariantCultureIgnoreCase) || model.Currency.Equals("SATS", StringComparison.InvariantCultureIgnoreCase))
            {
                return GetCryptoCodeInvoiceQrUrl(model.Name, GetFormattedBitcoinAmount(model.Amount, model.Currency), PaymentCryptoMethod.BTC);
            }
            else
            {
                return string.Empty;
            }
        }


        private decimal GetFormattedBitcoinAmount(decimal amount, string cryptoCode)
        {
            if (cryptoCode.Equals("SATS", StringComparison.InvariantCultureIgnoreCase))
            {
                amount = amount / SatoshisPerBitcoin;
            }
            return amount;
        }



        private string GetCryptoCodeInvoiceQrUrl(string destination, decimal amount, PaymentCryptoMethod cryptoMethod)
        {
            string invoiceUrl = string.Empty;
            switch (cryptoMethod)
            {
                case PaymentCryptoMethod.LNURL:
                    // To be handled later
                    break;
                case PaymentCryptoMethod.BTC:
                default:
                    invoiceUrl = $"bitcoin:{destination}?amount{amount}";
                    break;
            }
            return invoiceUrl;
        }
    }
}
