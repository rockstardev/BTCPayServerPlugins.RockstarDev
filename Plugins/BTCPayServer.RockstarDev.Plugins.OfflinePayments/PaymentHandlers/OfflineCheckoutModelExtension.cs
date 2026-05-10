using System.Linq;
using BTCPayServer.Payments;
using BTCPayServer.RockstarDev.Plugins.OfflinePayments.Services;

namespace BTCPayServer.RockstarDev.Plugins.OfflinePayments.PaymentHandlers;

public class OfflineCheckoutModelExtension(OfflineMethodConfigService configService, PaymentMethodId pmid) : ICheckoutModelExtension
{
    public PaymentMethodId PaymentMethodId { get; } = pmid;

    public string Image => "";
    public string Badge => "";

    public void ModifyCheckoutModel(CheckoutModelContext context)
    {
        if (context.Handler.PaymentMethodId != PaymentMethodId)
            return;

        var methodId = PaymentMethodId.ToString();
        var config = configService.GetEnabledMethodOptions(context.Model.StoreId).GetAwaiter().GetResult().FirstOrDefault(m => m.MethodId == methodId);
        if (config is null)
        {
            var entry = context.Model.AvailablePaymentMethods.FirstOrDefault(c => c.PaymentMethodId.ToString() == methodId);
            if (entry is not null)
                entry.Displayed = false;
            return;
        }

        context.Model.CheckoutBodyComponentName = $"OfflinePayment_{methodId}_Checkout";
        context.Model.InvoiceBitcoinUrlQR = null;
        context.Model.ExpirationSeconds = int.MaxValue;
        context.Model.Activated = true;
        context.Model.InvoiceBitcoinUrl = $"/plugins/{context.Model.StoreId}/offline-payments/{context.Model.InvoiceId}/mark-sent?methodId={methodId}";
        context.Model.ShowPayInWalletButton = false;
    }
}
