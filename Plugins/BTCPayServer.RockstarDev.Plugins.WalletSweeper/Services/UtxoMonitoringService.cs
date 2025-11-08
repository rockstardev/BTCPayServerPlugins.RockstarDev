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
                .Where(c => c.AutoEnabled)
                .Include(c => c.TrackedUtxos)
                .ToListAsync(cancellationToken);

            logger.LogInformation($"UtxoMonitoringService: Found {configs.Count} enabled configurations");

            foreach (var config in configs)
            {
                try
                {
                    var x = config.IntervalMinutes;
                    // TODO: Rename config.IntervalSeconds to config.IntervalMinutes and then trigger configurations based off that
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

        // Validate required fields
        if (string.IsNullOrEmpty(config.AccountXpub))
        {
            logger.LogWarning($"UtxoMonitoringService: Config {config.ConfigName} has no xpub configured, skipping");
            return;
        }

        var network = networkProvider.GetNetwork<BTCPayNetwork>("BTC");
        var explorerClient = explorerClientProvider.GetExplorerClient("BTC");

        try
        {
            // Get existing tracked UTXOs from our database
            var existingUtxos = await db.TrackedUtxos
                .Where(u => u.SweepConfigurationId == config.Id)
                .ToListAsync(cancellationToken);

            logger.LogInformation($"UtxoMonitoringService: Found {existingUtxos.Count} existing tracked UTXOs for {config.ConfigName}");

            // Sync with NBXplorer's UTXO set (NBXplorer is the source of truth)
            await DiscoverAndTrackUtxos(config, existingUtxos, network, explorerClient, db, cancellationToken);

            // Calculate current balance from unspent UTXOs
            var unspentUtxos = existingUtxos.Where(u => !u.IsSpent).ToList();
            config.CurrentBalance = unspentUtxos.Sum(u => u.Amount);
            config.LastMonitored = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                $"UtxoMonitoringService: Updated {config.ConfigName} - Balance: {config.CurrentBalance:N8} BTC from {unspentUtxos.Count} UTXOs");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"UtxoMonitoringService: Error monitoring {config.ConfigName}");
        }
    }

    /// <summary>
    /// Syncs our local UTXO tracking with NBXplorer's UTXO set.
    /// NBXplorer automatically:
    /// - Generates addresses based on the derivation scheme
    /// - Monitors the blockchain for transactions to those addresses
    /// - Maintains the current UTXO set
    /// We just need to query NBXplorer and update our local tracking records.
    /// </summary>
    private async Task DiscoverAndTrackUtxos(
        SweepConfiguration config,
        List<TrackedUtxo> existingUtxos,
        BTCPayNetwork network,
        ExplorerClient explorerClient,
        PluginDbContext db,
        CancellationToken cancellationToken)
    {
        try
        {
            // Parse the account xpub into a derivation strategy
            var derivationStrategy = network.NBXplorerNetwork.DerivationStrategyFactory.Parse(config.AccountXpub!);

            // Get current UTXO set from NBXplorer (the source of truth)
            // NBXplorer has already discovered all addresses and tracked all transactions
            var utxoChanges = await explorerClient.GetUTXOsAsync(derivationStrategy, cancellationToken);
            var unspentUtxos = utxoChanges.GetUnspentUTXOs();

            var newUtxosFound = 0;
            var spentUtxosMarked = 0;
            var confirmationsUpdated = 0;

            var currentUnspentOutpoints = unspentUtxos
                .Select(u => $"{u.Outpoint.Hash}:{u.Outpoint.N}")
                .ToHashSet();

            // Step 1: Check existing tracked UTXOs for spent status and confirmation updates
            foreach (var trackedUtxo in existingUtxos.Where(u => !u.IsSpent))
            {
                if (!currentUnspentOutpoints.Contains(trackedUtxo.Outpoint))
                {
                    // UTXO is no longer unspent - mark as spent
                    trackedUtxo.IsSpent = true;
                    trackedUtxo.SpentDate = DateTimeOffset.UtcNow;
                    trackedUtxo.UpdatedAt = DateTimeOffset.UtcNow;
                    spentUtxosMarked++;
                    
                    logger.LogInformation($"UtxoMonitoringService: UTXO spent for {config.ConfigName}: {trackedUtxo.Outpoint} = {trackedUtxo.Amount:N8} BTC");
                }
                else
                {
                    // UTXO still unspent - update confirmations if changed
                    var currentUtxo = unspentUtxos.First(u => $"{u.Outpoint.Hash}:{u.Outpoint.N}" == trackedUtxo.Outpoint);
                    var newConfirmations = (int)currentUtxo.Confirmations;
                    
                    if (trackedUtxo.Confirmations != newConfirmations)
                    {
                        trackedUtxo.Confirmations = newConfirmations;
                        trackedUtxo.UpdatedAt = DateTimeOffset.UtcNow;
                        confirmationsUpdated++;
                    }
                }
            }

            // Step 2: Add new UTXOs that we haven't tracked yet
            foreach (var utxo in unspentUtxos)
            {
                var outpoint = $"{utxo.Outpoint.Hash}:{utxo.Outpoint.N}";

                if (existingUtxos.All(u => u.Outpoint != outpoint))
                {
                    var trackedUtxo = new TrackedUtxo
                    {
                        Id = Guid.NewGuid().ToString(),
                        SweepConfigurationId = config.Id,
                        Outpoint = outpoint,
                        TxId = utxo.Outpoint.Hash.ToString(),
                        Vout = (int)utxo.Outpoint.N,
                        Amount = ((Money)utxo.Value).ToDecimal(MoneyUnit.BTC),
                        Address = utxo.ScriptPubKey.GetDestinationAddress(network.NBitcoinNetwork)?.ToString() ?? "",
                        DerivationPath = utxo.KeyPath?.ToString(),
                        Confirmations = (int)utxo.Confirmations,
                        ReceivedDate = utxo.Timestamp,
                        IsSpent = false
                    };

                    db.TrackedUtxos.Add(trackedUtxo);
                    existingUtxos.Add(trackedUtxo);
                    newUtxosFound++;

                    logger.LogInformation($"UtxoMonitoringService: New UTXO found for {config.ConfigName}: {outpoint} = {trackedUtxo.Amount:N8} BTC");
                }
            }

            logger.LogInformation($"UtxoMonitoringService: Scanned {config.ConfigName} - New: {newUtxosFound}, Spent: {spentUtxosMarked}, Confirmations updated: {confirmationsUpdated}");

            if (newUtxosFound > 0 || spentUtxosMarked > 0 || confirmationsUpdated > 0)
            {
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"UtxoMonitoringService: Error discovering UTXOs for {config.ConfigName}");
        }
    }
}
