using BTCPayServer.Services;

namespace BTCPayServer.RockstarDev.Plugins.CashCheckout.PaymentHandlers;

internal class CashTransactionLinkProvider(string blockExplorerLink) : DefaultTransactionLinkProvider(blockExplorerLink)
{
    public override string? GetTransactionLink(string paymentId)
    {
        return null;
    }
}