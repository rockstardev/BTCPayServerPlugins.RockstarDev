using System;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Data;
using BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Logic;
using BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Services;
using Microsoft.Extensions.DependencyInjection;
using Strike.Client;

namespace BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils
{
    public class RockstarStrikeUtilsPlugin : BaseBTCPayServerPlugin
    {
        public const string PluginStrikeNavKey = "RockstarStrikeUtilsNav";
        public const string PluginExchangeOrderNavKey = "PluginExchangeOrderNavKey";
        public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
        {
            new() {Identifier = nameof(BTCPayServer), Condition = ">=2.0.0"}
        };

        public override void Execute(IServiceCollection serviceCollection)
        {
            serviceCollection.AddUIExtension("store-integrations-nav", PluginStrikeNavKey);
            
            // strike registrations
            serviceCollection.AddStrikeHttpClient();
            serviceCollection.AddStrikeClient();

            serviceCollection.AddSingleton<StrikeClientFactory>();
            
            // Add the database related registrations
            serviceCollection.AddSingleton<RockstarStrikeDbContextFactory>();
            serviceCollection.AddDbContext<RockstarStrikeDbContext>((provider, o) =>
            {
                var factory = provider.GetRequiredService<RockstarStrikeDbContextFactory>();
                factory.ConfigureBuilder(o);
            });
            serviceCollection.AddHostedService<RockstarStrikeMigrationRunner>();
            
            // register heartbeat service and have it run every minute to catch periodic timer
            serviceCollection.AddSingleton<ExchangeOrderHeartbeatService>();
            serviceCollection.AddScheduledTask<ExchangeOrderHeartbeatService>(TimeSpan.FromMinutes(1));

            base.Execute(serviceCollection);
        }
    }
}