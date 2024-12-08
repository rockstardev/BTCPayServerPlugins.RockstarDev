using BTCPayServer.Payments;
using BTCPayServer.RockstarDev.Plugins.CashCheckoutMethod;

namespace BTCPayServer.RockstarDev.Plugins.CashCheckout.PaymentHandlers;
public class CashCheckoutModelExtension(CashCheckoutConfigurationItem configurationItem) : ICheckoutModelExtension
{
    public const string CheckoutBodyComponentName = "CASHCheckout";
    
    public PaymentMethodId PaymentMethodId { get; } = configurationItem.GetPaymentMethodId();
    public string Image => "";
    public string Badge => "";

    public void ModifyCheckoutModel(CheckoutModelContext context)
    {
        if (context is not { Handler: CashPaymentMethodHandler handler })
            return;
        
        context.Model.CheckoutBodyComponentName = CheckoutBodyComponentName;

        context.Model.InvoiceBitcoinUrlQR = null;
        context.Model.ExpirationSeconds = int.MaxValue;
        context.Model.Activated = true;
        
        context.Model.InvoiceBitcoinUrl = $"/stores/{context.Model.StoreId}/cash/MarkAsPaid?"+
                                          $"invoiceId={context.Model.InvoiceId}&"+
                                          $"returnUrl=/i/{context.Model.InvoiceId}";
        context.Model.ShowPayInWalletButton = true;
    }
}