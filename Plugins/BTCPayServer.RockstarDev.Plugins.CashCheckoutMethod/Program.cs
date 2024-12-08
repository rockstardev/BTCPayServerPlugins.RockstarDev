using System;
using System.Collections.Generic;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Hosting;
using BTCPayServer.Payments;
using BTCPayServer.Plugins;
using BTCPayServer.RockstarDev.Plugins.CashCheckoutMethod.PaymentHandlers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NBXplorer;

namespace BTCPayServer.RockstarDev.Plugins.CashCheckoutMethod
{
    public class CashCheckoutMethodPlugin : BaseBTCPayServerPlugin
    {
        public const string PluginNavKey = nameof(CashCheckoutMethodPlugin) + "Nav";
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
            var cashMethodConfigItem = new CashCheckoutConfigurationItem();
            services.AddSingleton(cashMethodConfigItem);
            
            var cashPaymentMethodId = cashMethodConfigItem.GetPaymentMethodId();
            //services.AddTransactionLinkProvider(tronUSDtPaymentMethodId, new TronUSDtTransactionLinkProvider(tronUSDtConfiguration.BlockExplorerLink));

            services.AddSingleton(provider => (IPaymentMethodHandler)ActivatorUtilities.CreateInstance(provider, typeof(CashPaymentMethodHandler),
                cashMethodConfigItem));
            services.AddSingleton<IPaymentLinkExtension>(provider =>
                (IPaymentLinkExtension)ActivatorUtilities.CreateInstance(provider, typeof(CashPaymentLinkExtension), cashPaymentMethodId));
            services.AddSingleton(provider =>
                (ICheckoutModelExtension)ActivatorUtilities.CreateInstance(provider, typeof(CashCheckoutModelExtension)));
        
            services.AddDefaultPrettyName(cashPaymentMethodId, cashMethodConfigItem.DisplayName);
            
            base.Execute(services);
        }
    }
}