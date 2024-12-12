using System;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Data;
using BTCPayServer.Hosting;
using BTCPayServer.Payments;
using BTCPayServer.Plugins;
using BTCPayServer.RockstarDev.Plugins.CashCheckout.PaymentHandlers;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NBXplorer;

namespace BTCPayServer.RockstarDev.Plugins.CashCheckout;

public class CashCheckoutPlugin : BaseBTCPayServerPlugin
{
    public const string PluginNavKey = nameof(CashCheckoutPlugin) + "Nav";
    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    {
        new() {Identifier = nameof(BTCPayServer), Condition = ">=2.0.0"}
    };

    internal static PaymentMethodId CashPmid = new PaymentMethodId("CASH");
    internal static string CashDisplayName = "Cash";

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