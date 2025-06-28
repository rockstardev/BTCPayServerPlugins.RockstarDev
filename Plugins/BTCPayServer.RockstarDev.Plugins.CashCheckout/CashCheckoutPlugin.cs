using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Hosting;
using BTCPayServer.Payments;
using BTCPayServer.RockstarDev.Plugins.CashCheckout.PaymentHandlers;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.RockstarDev.Plugins.CashCheckout;

public class CashCheckoutPlugin : BaseBTCPayServerPlugin
{
    public const string PluginNavKey = nameof(CashCheckoutPlugin) + "Nav";

    internal static PaymentMethodId CashPmid = new("CASH");
    internal static string CashDisplayName = "Cash";

    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    [
        new() { Identifier = nameof(BTCPayServer), Condition = ">=2.1.5" }
    ];

    public override void Execute(IServiceCollection services)
    {
        services.AddTransactionLinkProvider(CashPmid, new CashTransactionLinkProvider("cash"));

        services.AddSingleton(provider =>
            (IPaymentMethodHandler)ActivatorUtilities.CreateInstance(provider, typeof(CashPaymentMethodHandler)));
        services.AddSingleton(provider =>
            (ICheckoutModelExtension)ActivatorUtilities.CreateInstance(provider, typeof(CashCheckoutModelExtension)));

        services.AddDefaultPrettyName(CashPmid, CashDisplayName);

        //
        services.AddSingleton<CashStatusProvider>();

        //
        services.AddUIExtension("store-wallets-nav", "CashStoreNav");
        services.AddUIExtension("checkout-payment", "CashLikeMethodCheckout");

        base.Execute(services);
    }
}
