using BTCPayServer.Payments;

namespace BTCPayServer.RockstarDev.Plugins.CreditCheckout.PaymentHandlers;

public class CreditCheckoutModelExtension : ICheckoutModelExtension
{
    public const string CheckoutBodyComponentName = "CREDITCheckout";

    public PaymentMethodId PaymentMethodId => CreditCheckoutPlugin.CreditPmid;
    public string Image => "";
    public string Badge => "";

    public void ModifyCheckoutModel(CheckoutModelContext context)
    {
        if (context is not { Handler: CreditPaymentMethodHandler handler })
            return;

        context.Model.CheckoutBodyComponentName = CheckoutBodyComponentName;

        context.Model.InvoiceBitcoinUrlQR = null;
        context.Model.ExpirationSeconds = int.MaxValue;
        context.Model.Activated = true;

        context.Model.InvoiceBitcoinUrl = $"/stores/{context.Model.StoreId}/credit/MarkAsPaid?" +
                                          $"invoiceId={context.Model.InvoiceId}&" +
                                          $"returnUrl=/i/{context.Model.InvoiceId}";
        context.Model.ShowPayInWalletButton = true;
    }
}
