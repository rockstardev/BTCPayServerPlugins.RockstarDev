using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;

namespace BTCPayServer.RockstarDev.Plugins.CashCheckoutMethod.PaymentHandlers;
public class CashCheckoutModelExtension(CashCheckoutConfigurationItem configurationItem,
    IEnumerable<IPaymentLinkExtension> paymentLinkExtensions) : ICheckoutModelExtension
{
    private readonly IPaymentLinkExtension _paymentLinkExtension = 
        paymentLinkExtensions.Single(p => p.PaymentMethodId == configurationItem.GetPaymentMethodId());

    public PaymentMethodId PaymentMethodId { get; } = configurationItem.GetPaymentMethodId();
    public string Image => "";
    public string Badge => "";

    public void ModifyCheckoutModel(CheckoutModelContext context)
    {
        if (context is not { Handler: CashPaymentMethodHandler handler })
            return;
        
        context.Model.CheckoutBodyComponentName = BitcoinCheckoutModelExtension.CheckoutBodyComponentName;
        
        context.Model.InvoiceBitcoinUrl = _paymentLinkExtension.GetPaymentLink(context.Prompt, context.UrlHelper);
        context.Model.InvoiceBitcoinUrlQR = context.Model.InvoiceBitcoinUrl;
        context.Model.ShowPayInWalletButton = false;
        context.Model.PaymentMethodCurrency = configurationItem.DisplayName;
    }
}