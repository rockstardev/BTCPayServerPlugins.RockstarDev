using BTCPayServer.Payments;

namespace BTCPayServer.RockstarDev.Plugins.CashCheckoutMethod;

public class CashCheckoutConfigurationItem
{
    public PaymentMethodId GetPaymentMethodId() => new($"CASH");
    public string DisplayName => $"Cash";
    
    //
    public string Currency { get; init; } = "USD";
    public required int Divisibility { get; init; } = 2;
}