using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.RockstarDev.Plugins.WalletSweeper.Data;
using BTCPayServer.RockstarDev.Plugins.WalletSweeper.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBXplorer;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;

namespace BTCPayServer.RockstarDev.Plugins.WalletSweeper.Services;

/// <summary>
///     Monitors external wallets for UTXOs via NBXplorer WebSocket notifications
/// </summary>
public class UtxoMonitoringService(
    PluginDbContextFactory dbContextFactory,
    BTCPayNetworkProvider networkProvider,
    ExplorerClientProvider explorerClientProvider,
    ILogger<UtxoMonitoringService> logger) : IHostedService
{
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<string, DerivationStrategyBase> _trackedDerivations = new();
    private Task? _runningTask;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("UtxoMonitoringService: Starting NBXplorer WebSocket monitoring");

        // Load all tracked derivation schemes
        await LoadTrackedDerivations();

        // Start the WebSocket listener
        _runningTask = ListenToNBXplorer(_cts.Token);

        logger.LogInformation("UtxoMonitoringService: WebSocket monitoring started");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("UtxoMonitoringService: Stopping NBXplorer WebSocket monitoring");
        _cts.Cancel();

        if (_runningTask != null)
            try
            {
                await _runningTask;
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown
            }
    }

    private async Task LoadTrackedDerivations()
    {
        try
        {
            await using var db = dbContextFactory.CreateContext();
            var configs = await db.SweepConfigurations
                .Where(c => !string.IsNullOrEmpty(c.AccountXpub))
                .ToListAsync();

            var network = networkProvider.GetNetwork<BTCPayNetwork>("BTC");
            foreach (var config in configs)
                try
                {
                    var derivationStrategy = network.NBXplorerNetwork.DerivationStrategyFactory.Parse(config.AccountXpub!);
                    _trackedDerivations.TryAdd(config.Id, derivationStrategy);
                    logger.LogInformation($"UtxoMonitoringService: Tracking derivation for {config.ConfigName}");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, $"UtxoMonitoringService: Failed to parse derivation for {config.ConfigName}");
                }

            logger.LogInformation($"UtxoMonitoringService: Loaded {_trackedDerivations.Count} tracked derivations");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "UtxoMonitoringService: Error loading tracked derivations");
        }
    }

    private async Task ListenToNBXplorer(CancellationToken cancellationToken)
    {
        var network = networkProvider.GetNetwork<BTCPayNetwork>("BTC");
        var client = explorerClientProvider.GetExplorerClient("BTC");

        while (!cancellationToken.IsCancellationRequested)
            try
            {
                logger.LogInformation("UtxoMonitoringService: Connecting to NBXplorer WebSocket...");

                var session = await client.CreateWebsocketNotificationSessionLegacyAsync(cancellationToken);

                using (session)
                {
                    // Listen to all tracked sources (all derivation schemes NBXplorer knows about)
                    await session.ListenAllTrackedSourceAsync(cancellation: cancellationToken);
                    await session.ListenNewBlockAsync(cancellationToken);

                    logger.LogInformation("UtxoMonitoringService: Connected to NBXplorer WebSocket");

                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var newEvent = await session.NextEventAsync(cancellationToken);

                        switch (newEvent)
                        {
                            case NewTransactionEvent txEvent:
                                await HandleNewTransaction(txEvent, network, cancellationToken);
                                break;

                            case NewBlockEvent blockEvent:
                                await HandleNewBlock(blockEvent, network, cancellationToken);
                                break;
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Expected on shutdown
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "UtxoMonitoringService: WebSocket connection error, reconnecting in 10 seconds...");
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            }

        logger.LogInformation("UtxoMonitoringService: WebSocket listener stopped");
    }

    private async Task HandleNewTransaction(NewTransactionEvent txEvent, BTCPayNetwork network, CancellationToken cancellationToken)
    {
        if (txEvent.DerivationStrategy == null)
            return;

        // Find which config this derivation belongs to
        var configId = _trackedDerivations.FirstOrDefault(kvp => kvp.Value.ToString() == txEvent.DerivationStrategy.ToString()).Key;
        if (configId == null)
        {
            logger.LogDebug($"UtxoMonitoringService: Received transaction for untracked derivation: {txEvent.DerivationStrategy}");
            return;
        }

        logger.LogInformation($"UtxoMonitoringService: New transaction for config {configId} - TxId: {txEvent.TransactionData.TransactionHash}");

        try
        {
            await using var db = dbContextFactory.CreateContext();
            var config = await db.SweepConfigurations
                .Include(c => c.TrackedUtxos)
                .FirstOrDefaultAsync(c => c.Id == configId, cancellationToken);

            if (config != null) await MonitorConfiguration(config, db, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"UtxoMonitoringService: Error handling transaction for config {configId}");
        }
    }

    private async Task HandleNewBlock(NewBlockEvent blockEvent, BTCPayNetwork network, CancellationToken cancellationToken)
    {
        logger.LogInformation($"UtxoMonitoringService: New block {blockEvent.Height} - updating confirmations for all configs");

        try
        {
            await using var db = dbContextFactory.CreateContext();

            // Update confirmations for all tracked configs
            foreach (var configId in _trackedDerivations.Keys)
                try
                {
                    var config = await db.SweepConfigurations
                        .Include(c => c.TrackedUtxos)
                        .FirstOrDefaultAsync(c => c.Id == configId, cancellationToken);

                    if (config != null) await MonitorConfiguration(config, db, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, $"UtxoMonitoringService: Error updating config {configId} on new block");
                }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "UtxoMonitoringService: Error handling new block");
        }
    }

    /// <summary>
    ///     Manually trigger UTXO monitoring for a specific configuration
    ///     Useful for initial setup or on-demand checks
    /// </summary>
    public async Task MonitorConfiguration(
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

        // Add to tracked derivations if not already present (for WebSocket event filtering)
        try
        {
            var derivationStrategy = network.NBXplorerNetwork.DerivationStrategyFactory.Parse(config.AccountXpub!);
            _trackedDerivations.TryAdd(config.Id, derivationStrategy);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"UtxoMonitoringService: Failed to parse derivation for {config.ConfigName}");
        }

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
    ///     Syncs our local UTXO tracking with NBXplorer's UTXO set.
    ///     NBXplorer automatically:
    ///     - Generates addresses based on the derivation scheme
    ///     - Monitors the blockchain for transactions to those addresses
    ///     - Maintains the current UTXO set
    ///     We just need to query NBXplorer and update our local tracking records.
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

            logger.LogInformation(
                $"UtxoMonitoringService: Scanned {config.ConfigName} - New: {newUtxosFound}, Spent: {spentUtxosMarked}, Confirmations updated: {confirmationsUpdated}");

            if (newUtxosFound > 0 || spentUtxosMarked > 0 || confirmationsUpdated > 0) await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"UtxoMonitoringService: Error discovering UTXOs for {config.ConfigName}");
        }
    }
}
