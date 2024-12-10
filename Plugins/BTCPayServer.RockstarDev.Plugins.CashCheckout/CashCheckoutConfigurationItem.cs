using BTCPayServer.Payments;

namespace BTCPayServer.RockstarDev.Plugins.CashCheckout;

public class CashCheckoutConfigurationItem
{
    public PaymentMethodId GetPaymentMethodId() => new($"CASH");
    public string DisplayName => $"Cash";
    
    public required int Divisibility { get; init; } = 2;
}