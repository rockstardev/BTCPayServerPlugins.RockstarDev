using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Hosting;
using BTCPayServer.Payments;
using BTCPayServer.RockstarDev.Plugins.CreditCheckout.PaymentHandlers;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.RockstarDev.Plugins.CreditCheckout;

public class CreditCheckoutPlugin : BaseBTCPayServerPlugin
{
    public const string PluginNavKey = nameof(CreditCheckoutPlugin) + "Nav";

    internal static PaymentMethodId CreditPmid = new("CREDIT");
    internal static string CreditDisplayName = "Credit";

    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    [
        new() { Identifier = nameof(BTCPayServer), Condition = ">=2.1.5" }
    ];

    public override void Execute(IServiceCollection services)
    {
        services.AddTransactionLinkProvider(CreditPmid, new CreditTransactionLinkProvider("credit"));

        services.AddSingleton(provider =>
            (IPaymentMethodHandler)ActivatorUtilities.CreateInstance(provider, typeof(CreditPaymentMethodHandler)));
        services.AddSingleton(provider =>
            (ICheckoutModelExtension)ActivatorUtilities.CreateInstance(provider, typeof(CreditCheckoutModelExtension)));

        services.AddDefaultPrettyName(CreditPmid, CreditDisplayName);

        //
        services.AddSingleton<CreditStatusProvider>();

        //
        services.AddUIExtension("store-wallets-nav", "CreditStoreNav");
        services.AddUIExtension("checkout-payment", "CreditLikeMethodCheckout");

        base.Execute(services);
    }
}
