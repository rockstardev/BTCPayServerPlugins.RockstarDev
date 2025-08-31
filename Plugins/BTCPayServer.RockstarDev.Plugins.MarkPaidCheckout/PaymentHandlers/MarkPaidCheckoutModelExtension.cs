using System.Text.RegularExpressions;
using BTCPayServer.Payments;

namespace BTCPayServer.RockstarDev.Plugins.MarkPaidCheckout.PaymentHandlers;

public static class MarkPaidUi
{
    private static readonly Regex NonWord = new("[^A-Za-z0-9_]", RegexOptions.Compiled);
    public static string ComponentNameFor(string method) => $"MP_{NonWord.Replace(method ?? "", "")}_Checkout";
}

public class MarkPaidCheckoutModelExtension(PaymentMethodId pmid) : ICheckoutModelExtension
{
    public PaymentMethodId PaymentMethodId { get; } = pmid;
    public string Image => "";
    public string Badge => "";

    public void ModifyCheckoutModel(CheckoutModelContext context)
    {
        if (context.Handler.PaymentMethodId != PaymentMethodId)
            return;
        var method = PaymentMethodId.ToString();
        context.Model.CheckoutBodyComponentName = MarkPaidUi.ComponentNameFor(method);
        context.Model.InvoiceBitcoinUrlQR = null;
        context.Model.ExpirationSeconds = int.MaxValue;
        context.Model.Activated = true;
        context.Model.InvoiceBitcoinUrl = $"/stores/{context.Model.StoreId}/markpaid/MarkAsPaid?invoiceId={context.Model.InvoiceId}&returnUrl=/i/{context.Model.InvoiceId}&method={method}";
        context.Model.ShowPayInWalletButton = true;
    }
}
