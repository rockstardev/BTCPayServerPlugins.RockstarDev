using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Payments;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.RockstarDev.Plugins.CashCheckout;

public class CounterPlugin : BaseBTCPayServerPlugin
{
    public const string PluginNavKey = nameof(CounterPlugin) + "Nav";

    internal static PaymentMethodId CashPmid = new("CASH");
    internal static string CashDisplayName = "Cash";

    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    [
        new() { Identifier = nameof(BTCPayServer), Condition = ">=2.0.0" }
    ];

    public override void Execute(IServiceCollection services)
    {
        //services.AddTransactionLinkProvider(CashPmid, new CashTransactionLinkProvider("cash"));

        //services.AddSingleton(provider =>
        //    (IPaymentMethodHandler)ActivatorUtilities.CreateInstance(provider, typeof(CashPaymentMethodHandler)));
        //services.AddSingleton(provider =>
        //    (ICheckoutModelExtension)ActivatorUtilities.CreateInstance(provider, typeof(CashCheckoutModelExtension)));

        //services.AddDefaultPrettyName(CashPmid, CashDisplayName);

        ////
        //services.AddSingleton<CashStatusProvider>();

        //
        services.AddUIExtension("server-nav", "PluginCounterNav");

        base.Execute(services);
    }
}
