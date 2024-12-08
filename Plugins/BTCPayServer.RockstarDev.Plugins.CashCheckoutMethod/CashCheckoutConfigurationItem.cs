using BTCPayServer.Payments;

namespace BTCPayServer.RockstarDev.Plugins.CashCheckoutMethod;

public class CashCheckoutConfigurationItem
{
    public PaymentMethodId GetPaymentMethodId() => new($"CASH");
    public string DisplayName => $"Cash";
}