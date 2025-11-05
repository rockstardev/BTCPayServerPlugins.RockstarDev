using System;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.RockstarDev.Plugins.WalletSweeper.Data;
using BTCPayServer.RockstarDev.Plugins.WalletSweeper.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.RockstarDev.Plugins.WalletSweeper;

public class WalletSweeperPlugin : BaseBTCPayServerPlugin
{
    public const string PluginNavKey = nameof(WalletSweeperPlugin) + "Nav";

    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    [
        new() { Identifier = nameof(BTCPayServer), Condition = ">=2.1.5" }
    ];

    public override void Execute(IServiceCollection serviceCollection)
    {
        // Add UI extension for store integrations menu
        serviceCollection.AddUIExtension("store-integrations-nav", PluginNavKey);

        // Add the database related registrations
        serviceCollection.AddSingleton<PluginDbContextFactory>();
        serviceCollection.AddDbContext<PluginDbContext>((provider, o) =>
        {
            var factory = provider.GetRequiredService<PluginDbContextFactory>();
            factory.ConfigureBuilder(o);
        });
        serviceCollection.AddHostedService<PluginMigrationRunner>();

        // Add services
        serviceCollection.AddSingleton<SeedEncryptionService>();
        serviceCollection.AddSingleton<WalletSweeperService>();

        // Add the UTXO monitoring background service
        // Configurable via environment variable BTCPAY_WALLETSWEEPER_INTERVAL (in seconds)
        // Default: 600 seconds (10 minutes)
        var intervalSeconds = int.TryParse(Environment.GetEnvironmentVariable("BTCPAY_WALLETSWEEPER_INTERVAL"), out var seconds) && seconds > 0
            ? seconds
            : 600;
        serviceCollection.AddScheduledTask<UtxoMonitoringService>(TimeSpan.FromSeconds(intervalSeconds));

        base.Execute(serviceCollection);
    }
}
