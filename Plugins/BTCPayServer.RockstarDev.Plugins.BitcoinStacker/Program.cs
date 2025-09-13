using System;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Data;
using BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Logic;
using BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Strike.Client;

namespace BTCPayServer.RockstarDev.Plugins.BitcoinStacker;

// ReSharper disable once ClassNeverInstantiated.Global
public class BitcoinStackerPlugin : BaseBTCPayServerPlugin
{
    public const string PluginNavKey = nameof(BitcoinStackerPlugin) + "Nav";

    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    [
        new() { Identifier = nameof(BTCPayServer), Condition = ">=2.1.5" }
    ];

    public override void Execute(IServiceCollection serviceCollection)
    {
        serviceCollection.AddUIExtension("store-integrations-nav", PluginNavKey);

        // strike registrations
        serviceCollection.AddStrikeHttpClient();
        serviceCollection.AddStrikeClient();
        serviceCollection.AddSingleton<StrikeClientFactory>();

        // stripe registrations
        serviceCollection.AddSingleton<StripeClientFactory>();

        // Add the database related registrations
        serviceCollection.AddSingleton<PluginDbContextFactory>();
        serviceCollection.AddDbContext<PluginDbContext>((provider, o) =>
        {
            var factory = provider.GetRequiredService<PluginDbContextFactory>();
            factory.ConfigureBuilder(o);
        });
        serviceCollection.AddHostedService<PluginMigrationRunner>();

        // heartbeat service
        //serviceCollection.AddSingleton<IHostedService, ExchangeOrderHeartbeatService>();
        serviceCollection.AddSingleton<IHostedService>(provider => provider.GetService<ExchangeOrderHeartbeatService>());
        serviceCollection.AddScheduledTask<ExchangeOrderHeartbeatService>(TimeSpan.FromMinutes(1));
        base.Execute(serviceCollection);
    }
}
