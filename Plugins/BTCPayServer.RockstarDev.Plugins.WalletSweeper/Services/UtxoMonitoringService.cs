using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.HostedServices;
using BTCPayServer.RockstarDev.Plugins.WalletSweeper.Data;
using BTCPayServer.RockstarDev.Plugins.WalletSweeper.Data.Models;
using BTCPayServer.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBXplorer;

namespace BTCPayServer.RockstarDev.Plugins.WalletSweeper.Services;

/// <summary>
/// Monitors external wallets for UTXOs and updates the database
/// </summary>
public class UtxoMonitoringService(
    PluginDbContextFactory dbContextFactory,
    BTCPayNetworkProvider networkProvider,
    ExplorerClientProvider explorerClientProvider,
    ILogger<UtxoMonitoringService> logger) : IPeriodicTask
{
    public async Task Do(CancellationToken cancellationToken)
    {
        logger.LogInformation("UtxoMonitoringService: Starting periodic check");
        
        try
        {
            await using var db = dbContextFactory.CreateContext();

            // Get all enabled configurations
            var configs = await db.SweepConfigurations
                .Where(c => c.Enabled)
                .Include(c => c.TrackedUtxos)
                .ToListAsync(cancellationToken);

            logger.LogInformation($"UtxoMonitoringService: Found {configs.Count} enabled configurations");

            foreach (var config in configs)
            {
                try
                {
                    await MonitorConfiguration(config, db, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, $"UtxoMonitoringService: Error monitoring config {config.Id} ({config.ConfigName})");
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "UtxoMonitoringService: Error in periodic task");
        }
    }

    private async Task MonitorConfiguration(
        SweepConfiguration config,
        PluginDbContext db,
        CancellationToken cancellationToken)
    {
        logger.LogInformation($"UtxoMonitoringService: Monitoring config {config.ConfigName} (ID: {config.Id})");

        if (string.IsNullOrEmpty(config.EncryptedSeed))
        {
            logger.LogWarning($"UtxoMonitoringService: Config {config.ConfigName} has no seed configured, skipping");
            return;
        }

        if (string.IsNullOrEmpty(config.DerivationPath))
        {
            logger.LogWarning($"UtxoMonitoringService: Config {config.ConfigName} has no derivation path, skipping");
            return;
        }

        var network = networkProvider.GetNetwork<BTCPayNetwork>("BTC");
        var explorerClient = explorerClientProvider.GetExplorerClient("BTC");

        // Derive addresses from seed (we'll need to decrypt seed first - for now, skip)
        // TODO: Implement address derivation and UTXO discovery
        
        // For now, just update last monitored timestamp
        config.LastMonitored = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        
        logger.LogInformation($"UtxoMonitoringService: Updated last monitored for {config.ConfigName}");
    }
}
