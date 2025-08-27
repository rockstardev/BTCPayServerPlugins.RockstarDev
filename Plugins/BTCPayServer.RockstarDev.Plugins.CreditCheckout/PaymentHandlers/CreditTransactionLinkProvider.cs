using BTCPayServer.Services;

namespace BTCPayServer.RockstarDev.Plugins.CreditCheckout.PaymentHandlers;

internal class CreditTransactionLinkProvider(string blockExplorerLink) : DefaultTransactionLinkProvider(blockExplorerLink)
{
    public override string? GetTransactionLink(string paymentId)
    {
        return null;
    }
}
