﻿using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Data;
using BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Logic;
using Microsoft.Extensions.DependencyInjection;
using Strike.Client;

namespace BTCPayServer.RockstarDev.Plugins.BitcoinStacker;

// ReSharper disable once ClassNeverInstantiated.Global
public class BitcoinStackerPlugin : BaseBTCPayServerPlugin
{
    public const string PluginNavKey = nameof(BitcoinStackerPlugin) + "Nav";
    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    [
        new() {Identifier = nameof(BTCPayServer), Condition = ">=2.0.0"}
    ];

    public override void Execute(IServiceCollection serviceCollection)
    {
        serviceCollection.AddUIExtension("store-integrations-nav", "PluginNav");
        
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
        
        base.Execute(serviceCollection);
    }
}