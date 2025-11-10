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
        
        // UTXO monitoring via NBXplorer WebSocket (real-time, event-driven)
        // Register as singleton first so it can be injected into controllers
        serviceCollection.AddSingleton<UtxoMonitoringService>();
        serviceCollection.AddHostedService(provider => provider.GetRequiredService<UtxoMonitoringService>());

        // Auto-sweep checking (periodic)
        // Configurable via environment variable BTCPAY_WALLETSWEEPER_INTERVAL (in seconds)
        var intervalSeconds = int.TryParse(Environment.GetEnvironmentVariable("BTCPAY_WALLETSWEEPER_INTERVAL"), out var seconds) && seconds > 0
            ? seconds
            : 60; // Default: 60 seconds (check for auto-sweeps every minute)
        serviceCollection.AddScheduledTask<WalletSweeperService>(TimeSpan.FromSeconds(intervalSeconds));

        base.Execute(serviceCollection);
    }
}
