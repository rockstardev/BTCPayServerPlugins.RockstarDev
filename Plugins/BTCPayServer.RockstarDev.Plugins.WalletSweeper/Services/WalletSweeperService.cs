using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.HostedServices;
using BTCPayServer.RockstarDev.Plugins.WalletSweeper.Data;
using BTCPayServer.RockstarDev.Plugins.WalletSweeper.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.RockstarDev.Plugins.WalletSweeper.Services;

public class WalletSweeperService(
    PluginDbContextFactory dbContextFactory,
    ILogger<WalletSweeperService> logger)
    : IPeriodicTask
{
    public async Task Do(CancellationToken cancellationToken)
    {
        logger.LogInformation("WalletSweeper: Starting periodic check");

        try
        {
            await using var db = dbContextFactory.CreateContext();

            // Get all enabled configurations
            var configs = await db.SweepConfigurations
                .Where(c => c.Enabled)
                .ToListAsync(cancellationToken);

            logger.LogInformation($"WalletSweeper: Found {configs.Count} enabled configurations");

            foreach (var config in configs)
            {
                try
                {
                    await ProcessConfiguration(config, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, $"WalletSweeper: Error processing configuration {config.Id} for store {config.StoreId}");
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "WalletSweeper: Error in periodic task");
        }
    }

    private async Task ProcessConfiguration(SweepConfiguration config, CancellationToken cancellationToken)
    {
        logger.LogInformation($"WalletSweeper: Processing config {config.Id} for store {config.StoreId}");

        // Check if enough time has passed since last sweep
        if (config.LastSweepDate.HasValue)
        {
            var daysSinceLastSweep = (DateTimeOffset.UtcNow - config.LastSweepDate.Value).TotalDays;
            if (daysSinceLastSweep < config.IntervalDays)
            {
                logger.LogInformation($"WalletSweeper: Skipping - only {daysSinceLastSweep:F1} days since last sweep (interval: {config.IntervalDays} days)");
                return;
            }
        }

        // TODO: Get wallet balance
        var currentBalance = await GetWalletBalance(config.StoreId, cancellationToken);
        
        logger.LogInformation($"WalletSweeper: Store {config.StoreId} balance: {currentBalance} BTC");

        // Check minimum balance threshold
        if (currentBalance < config.MinimumBalance)
        {
            logger.LogInformation($"WalletSweeper: Balance {currentBalance} is below minimum {config.MinimumBalance}, skipping");
            return;
        }

        // Determine trigger type
        var triggerType = currentBalance >= config.MaximumBalance 
            ? SweepHistory.TriggerTypes.MaxThreshold 
            : SweepHistory.TriggerTypes.Scheduled;

        // Execute sweep
        await ExecuteSweep(config, currentBalance, triggerType, cancellationToken);
    }

    private async Task<decimal> GetWalletBalance(string storeId, CancellationToken cancellationToken)
    {
        // TODO: Implement actual wallet balance fetching
        // This is a placeholder
        logger.LogWarning($"WalletSweeper: GetWalletBalance not implemented yet for store {storeId}");
        return 0m;
    }

    private async Task ExecuteSweep(
        SweepConfiguration config, 
        decimal currentBalance, 
        SweepHistory.TriggerTypes triggerType,
        CancellationToken cancellationToken)
    {
        logger.LogInformation($"WalletSweeper: Executing sweep for store {config.StoreId}, trigger: {triggerType}");

        await using var db = dbContextFactory.CreateContext();

        var history = new SweepHistory
        {
            Id = Guid.NewGuid(),
            ConfigurationId = config.Id,
            StoreId = config.StoreId,
            Timestamp = DateTimeOffset.UtcNow,
            TriggerType = triggerType,
            Status = SweepHistory.SweepStatuses.Pending,
            Destination = config.DestinationValue
        };

        try
        {
            // Calculate sweep amount (balance - reserve - estimated fee)
            var sweepAmount = currentBalance - config.ReserveAmount;
            
            // TODO: Estimate fee based on config.FeeRate
            var estimatedFee = 0.0001m; // Placeholder
            
            sweepAmount -= estimatedFee;

            if (sweepAmount <= 0)
            {
                throw new InvalidOperationException("Sweep amount is zero or negative after accounting for reserve and fees");
            }

            history.Amount = sweepAmount;
            history.Fee = estimatedFee;

            // TODO: Execute actual transaction
            // 1. Check if hot wallet or decrypt seed
            // 2. Create transaction
            // 3. Sign transaction
            // 4. Broadcast transaction
            // 5. Get TxId

            history.TxId = "placeholder_txid"; // TODO: Replace with actual TxId
            history.Status = SweepHistory.SweepStatuses.Success;

            // Update last sweep date
            config.LastSweepDate = DateTimeOffset.UtcNow;
            db.SweepConfigurations.Update(config);

            logger.LogInformation($"WalletSweeper: Sweep successful! Amount: {sweepAmount} BTC, Fee: {estimatedFee} BTC");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"WalletSweeper: Sweep failed for store {config.StoreId}");
            history.Status = SweepHistory.SweepStatuses.Failed;
            history.ErrorMessage = ex.Message;
        }

        db.SweepHistories.Add(history);
        await db.SaveChangesAsync(cancellationToken);
    }
}
