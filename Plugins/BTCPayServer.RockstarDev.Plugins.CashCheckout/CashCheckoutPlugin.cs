using System;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Data;
using BTCPayServer.Hosting;
using BTCPayServer.Payments;
using BTCPayServer.Plugins;
using BTCPayServer.RockstarDev.Plugins.CashCheckout.PaymentHandlers;
using BTCPayServer.RockstarDev.Plugins.CashCheckoutMethod;
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

    public override void Execute(IServiceCollection services)
    {
            
        // get dependencies
        var networkProvider = ((PluginServiceCollection)services).BootstrapServices.GetRequiredService<NBXplorerNetworkProvider>();
        var configuration = ((PluginServiceCollection)services).BootstrapServices.GetRequiredService<IConfiguration>();
        var settingsRepository = services.BuildServiceProvider().GetService<ISettingsRepository>() ??
                                 throw new InvalidOperationException("serviceProvider.GetService<ISettingsRepository>()");

        // initialize configuration
        var cashMethodConfigItem = new CashCheckoutConfigurationItem
        {
            Divisibility = 2
        };
        services.AddSingleton(cashMethodConfigItem);
            
        var cashPaymentMethodId = cashMethodConfigItem.GetPaymentMethodId();
        services.AddTransactionLinkProvider(cashPaymentMethodId, new CashTransactionLinkProvider("cash"));

        services.AddSingleton(provider => (IPaymentMethodHandler)ActivatorUtilities.CreateInstance(provider, typeof(CashPaymentMethodHandler),
            cashMethodConfigItem));
        services.AddSingleton(provider =>
            (ICheckoutModelExtension)ActivatorUtilities.CreateInstance(provider, typeof(CashCheckoutModelExtension)));
        
        services.AddDefaultPrettyName(cashPaymentMethodId, cashMethodConfigItem.DisplayName);
            
        //
        services.AddSingleton<CashStatusProvider>();
        
        //
        services.AddUIExtension("store-wallets-nav", "CashStoreNav");
        services.AddUIExtension("checkout-payment", "CashLikeMethodCheckout");
        //services.AddUIExtension("checkout-bitcoin-post-content", "Dammit");
            
        base.Execute(services);
    }
}

internal class CashTransactionLinkProvider(string blockExplorerLink) : DefaultTransactionLinkProvider(blockExplorerLink)
{
    public override string? GetTransactionLink(string paymentId)
    {
        return null;
    }
}

public class CashStatusProvider(StoreRepository storeRepository,
    CashCheckoutConfigurationItem cashMethod,
    PaymentMethodHandlerDictionary handlers)
{
    public async Task<bool> CashEnabled(string? storeId)
    {
        try
        {
            var storeData = await storeRepository.FindStore(storeId);
            var currentPaymentMethodConfig =
                storeData.GetPaymentMethodConfig<CashPaymentMethodConfig>(cashMethod.GetPaymentMethodId(), handlers);

            var excludeFilters = storeData.GetStoreBlob().GetExcludedPaymentMethods();
            var enabled = !excludeFilters.Match(cashMethod.GetPaymentMethodId());

            return enabled;
        }
        catch
        {
            return false;
        }
    }
}