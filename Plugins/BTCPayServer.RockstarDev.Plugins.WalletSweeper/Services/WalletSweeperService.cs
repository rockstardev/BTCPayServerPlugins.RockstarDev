using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Payments;
using BTCPayServer.RockstarDev.Plugins.WalletSweeper.Data;
using BTCPayServer.RockstarDev.Plugins.WalletSweeper.Data.Models;
using BTCPayServer.Services;
using BTCPayServer.Services.Fees;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBXplorer;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;

namespace BTCPayServer.RockstarDev.Plugins.WalletSweeper.Services;

public class WalletSweeperService(
    PluginDbContextFactory dbContextFactory,
    StoreRepository storeRepository,
    BTCPayWalletProvider walletProvider,
    BTCPayNetworkProvider networkProvider,
    ExplorerClientProvider explorerClientProvider,
    IFeeProviderFactory feeProviderFactory,
    PaymentMethodHandlerDictionary handlers,
    ILogger<WalletSweeperService> logger)
    : IPeriodicTask
{
    public async Task Do(CancellationToken cancellationToken)
    {
        // logger.LogInformation("WalletSweeper: Starting periodic check");
        //
        // try
        // {
        //     await using var db = dbContextFactory.CreateContext();
        //
        //     // Get all enabled configurations
        //     var configs = await db.SweepConfigurations
        //         .Where(c => c.Enabled)
        //         .ToListAsync(cancellationToken);
        //
        //     logger.LogInformation($"WalletSweeper: Found {configs.Count} enabled configurations");
        //
        //     foreach (var config in configs)
        //     {
        //         try
        //         {
        //             await ProcessConfiguration(config, cancellationToken);
        //         }
        //         catch (Exception ex)
        //         {
        //             logger.LogError(ex, $"WalletSweeper: Error processing configuration {config.Id} for store {config.StoreId}");
        //         }
        //     }
        // }
        // catch (Exception ex)
        // {
        //     logger.LogError(ex, "WalletSweeper: Error in periodic task");
        // }
    }

    /// <summary>
    /// Manually trigger a sweep for a specific store
    /// </summary>
    public async Task TriggerManualSweep(string storeId, CancellationToken cancellationToken = default)
    {
        logger.LogInformation($"WalletSweeper: Manual sweep triggered for store {storeId}");

        await using var db = dbContextFactory.CreateContext();

        var config = await db.SweepConfigurations
            .FirstOrDefaultAsync(c => c.StoreId == storeId && c.Enabled, cancellationToken);

        if (config == null)
        {
            throw new InvalidOperationException($"No enabled sweep configuration found for store {storeId}");
        }

        // Get wallet balance
        var currentBalance = await GetWalletBalance(config.StoreId, cancellationToken);
        
        // Execute sweep with Manual trigger type, bypassing all checks
        await ExecuteSweep(config, currentBalance, SweepHistory.TriggerTypes.Manual, cancellationToken);
    }

    private async Task ProcessConfiguration(SweepConfiguration config, CancellationToken cancellationToken)
    {
        logger.LogInformation($"WalletSweeper: Processing config {config.Id} for store {config.StoreId}");

        // Get wallet balance
        var currentBalance = await GetWalletBalance(config.StoreId, cancellationToken);
        logger.LogInformation($"WalletSweeper: Store {config.StoreId} balance: {currentBalance} BTC");

        // Determine if sweep should be executed
        var triggerType = ShouldExecuteSweep(config, currentBalance);
        if (triggerType != null)
        {
            await ExecuteSweep(config, currentBalance, triggerType.Value, cancellationToken);
        }
    }

    private SweepHistory.TriggerTypes? ShouldExecuteSweep(
        SweepConfiguration config,
        decimal currentBalance)
    {
        // Check 1: Balance below minimum threshold
        if (currentBalance < config.MinimumBalance)
        {
            logger.LogInformation($"WalletSweeper: Balance {currentBalance} BTC is below minimum {config.MinimumBalance} BTC, skipping");
            return null;
        }

        // Check 2: Balance not high enough above reserve to justify sweep
        var minViableBalance = config.ReserveAmount + 0.0001m; // Reserve + min dust threshold + estimated fee
        if (currentBalance <= minViableBalance)
        {
            logger.LogInformation(
                $"WalletSweeper: Balance {currentBalance} BTC is not high enough above reserve {config.ReserveAmount} BTC to justify sweep, skipping");
            return null;
        }

        // Check 3: Max threshold exceeded - immediate sweep required
        if (currentBalance >= config.MaximumBalance)
        {
            logger.LogInformation($"WalletSweeper: Max threshold {config.MaximumBalance} BTC exceeded! Sweep required.");
            return SweepHistory.TriggerTypes.MaxThreshold;
        }

        // Check 4: Scheduled sweep - verify interval has passed
        if (config.LastSweepDate.HasValue)
        {
            var daysSinceLastSweep = (DateTimeOffset.UtcNow - config.LastSweepDate.Value).TotalDays;
            if (daysSinceLastSweep < config.IntervalDays)
            {
                logger.LogInformation(
                    $"WalletSweeper: Scheduled sweep not due - only {daysSinceLastSweep:F1} days since last sweep (interval: {config.IntervalDays} days)");
                return null;
            }
            else
            {
                logger.LogInformation($"WalletSweeper: Scheduled sweep is due, sweep required.");
                return SweepHistory.TriggerTypes.Scheduled;
            }
        }

        return null;
    }

    private async Task<decimal> GetWalletBalance(string storeId, CancellationToken cancellationToken)
    {
        try
        {
            var store = await storeRepository.FindStore(storeId);
            if (store == null)
            {
                logger.LogWarning($"WalletSweeper: Store {storeId} not found");
                return 0m;
            }

            var derivation = store.GetDerivationSchemeSettings(handlers, "BTC");
            if (derivation == null)
            {
                logger.LogWarning($"WalletSweeper: No BTC derivation scheme found for store {storeId}");
                return 0m;
            }

            var wallet = walletProvider.GetWallet("BTC");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            
            var balanceData = await wallet.GetBalance(derivation.AccountDerivation, cts.Token);
            var money = balanceData.Available ?? balanceData.Total;
            
            if (money is Money btcMoney)
            {
                return btcMoney.ToDecimal(MoneyUnit.BTC);
            }

            return 0m;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"WalletSweeper: Error fetching wallet balance for store {storeId}");
            return 0m;
        }
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
            // Get actual fee rate estimate
            var network = networkProvider.GetNetwork<BTCPayNetwork>("BTC");
            var feeRate = await GetFeeRateAsync(network, config.FeeRate, cancellationToken);
            
            // Estimate fee (rough estimate: ~200 vbytes for typical sweep transaction)
            var estimatedFee = (feeRate.SatoshiPerByte * 200m) / 100_000_000m; // Convert sat to BTC

            // Calculate sweep amount: current balance - reserve amount - fee
            // This leaves exactly the reserve amount in the wallet after the sweep
            var sweepAmount = currentBalance - config.ReserveAmount - estimatedFee;

            if (sweepAmount <= 0)
            {
                throw new InvalidOperationException(
                    $"Sweep amount is zero or negative after accounting for reserve and fees. Balance: {currentBalance}, Reserve: {config.ReserveAmount}, Fee: {estimatedFee}");
            }

            history.Amount = sweepAmount;
            history.Fee = estimatedFee;

            logger.LogInformation(
                $"WalletSweeper: Calculated sweep - Balance: {currentBalance} BTC, Reserve: {config.ReserveAmount} BTC, Fee: {estimatedFee} BTC, Sweep Amount: {sweepAmount} BTC");

            // Get store and derivation settings
            var store = await storeRepository.FindStore(config.StoreId);
            if (store == null)
            {
                throw new InvalidOperationException($"Store {config.StoreId} not found");
            }

            var derivation = store.GetDerivationSchemeSettings(handlers, "BTC");
            if (derivation == null)
            {
                throw new InvalidOperationException($"No BTC wallet configured for store {config.StoreId}");
            }

            // Check if hot wallet
            if (!derivation.IsHotWallet)
            {
                throw new InvalidOperationException("Cold wallet sweeping is not yet implemented");
            }

            var explorerClient = explorerClientProvider.GetExplorerClient("BTC");

            // Get signing key from NBXplorer
            var signingKeyStr = await explorerClient.GetMetadataAsync<string>(
                derivation.AccountDerivation,
                WellknownMetadataKeys.MasterHDKey,
                cancellationToken);

            if (signingKeyStr == null)
            {
                throw new InvalidOperationException("Hot wallet signing key not found in NBXplorer");
            }

            // Parse destination address
            var destinationAddress = BitcoinAddress.Create(config.DestinationValue, network.NBitcoinNetwork);

            // Create PSBT
            var createPSBTRequest = new CreatePSBTRequest
            {
                RBF = true,
                AlwaysIncludeNonWitnessUTXO = derivation.DefaultIncludeNonWitnessUtxo,
                IncludeGlobalXPub = derivation.IsMultiSigOnServer,
                Destinations = new List<CreatePSBTDestination>
                {
                    new CreatePSBTDestination
                    {
                        Destination = destinationAddress.ScriptPubKey,
                        Amount = Money.Coins(sweepAmount),
                        //SweepAll = false
                    }
                },
                FeePreference = new FeePreference
                {
                    ExplicitFeeRate = feeRate
                }
            };

            logger.LogInformation($"WalletSweeper: Creating PSBT for sweep to {config.DestinationValue}");
            var psbtResponse = await explorerClient.CreatePSBTAsync(derivation.AccountDerivation, createPSBTRequest, cancellationToken);
            var psbt = psbtResponse.PSBT;

            // Sign the PSBT
            var extKey = ExtKey.Parse(signingKeyStr, network.NBitcoinNetwork);
            var signingKeySettings = derivation.GetAccountKeySettingsFromRoot(extKey);
            if (signingKeySettings == null)
            {
                throw new InvalidOperationException("Could not derive signing key from root key");
            }

            var rootedKeyPath = signingKeySettings.GetRootedKeyPath();
            if (rootedKeyPath == null)
            {
                throw new InvalidOperationException("Could not determine rooted key path");
            }

            psbt.RebaseKeyPaths(signingKeySettings.AccountKey, rootedKeyPath);
            var signingKey = extKey.Derive(rootedKeyPath.KeyPath);

            psbt.Settings.SigningOptions = new SigningOptions { EnforceLowR = true };
            var signed = psbt.SignAll(derivation.AccountDerivation, signingKey, rootedKeyPath);

            if (signed == null)
            {
                throw new InvalidOperationException("Failed to sign PSBT");
            }

            // Finalize PSBT
            if (!psbt.TryFinalize(out var errors))
            {
                throw new InvalidOperationException($"Failed to finalize PSBT: {string.Join(", ", errors)}");
            }

            // Extract and broadcast transaction
            var transaction = psbt.ExtractTransaction();
            logger.LogInformation($"WalletSweeper: Broadcasting transaction {transaction.GetHash()}");

            var broadcastResult = await explorerClient.BroadcastAsync(transaction, cancellationToken);
            if (!broadcastResult.Success)
            {
                throw new InvalidOperationException(
                    $"Broadcast failed: {broadcastResult.RPCCode} {broadcastResult.RPCCodeMessage} {broadcastResult.RPCMessage}");
            }

            // Update actual fee from transaction
            var actualFee = psbt.GetFee();
            history.Fee = actualFee.ToDecimal(MoneyUnit.BTC);
            history.TxId = transaction.GetHash().ToString();
            history.Status = SweepHistory.SweepStatuses.Success;

            // Invalidate wallet cache
            var wallet = walletProvider.GetWallet("BTC");
            wallet.InvalidateCache(derivation.AccountDerivation);

            logger.LogInformation($"WalletSweeper: Sweep successful! TxId: {history.TxId}, Actual Fee: {history.Fee} BTC");

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

    private async Task<FeeRate> GetFeeRateAsync(
        BTCPayNetwork network,
        SweepConfiguration.FeeRates feeRateType,
        CancellationToken cancellationToken)
    {
        try
        {
            var feeProvider = feeProviderFactory.CreateFeeProvider(network);
            
            // Map fee rate type to block target
            var blockTarget = feeRateType switch
            {
                SweepConfiguration.FeeRates.Economy => 100,   // ~17 hours
                SweepConfiguration.FeeRates.Normal => 6,      // ~1 hour
                SweepConfiguration.FeeRates.Priority => 1,    // ~10 minutes
                _ => 6
            };
            
            var feeRate = await feeProvider.GetFeeRateAsync(blockTarget);
            logger.LogInformation($"WalletSweeper: Fetched fee rate for {feeRateType} ({blockTarget} blocks): {feeRate.SatoshiPerByte} sat/vB");
            
            return feeRate;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, $"WalletSweeper: Failed to fetch dynamic fee rate, using fallback");
            
            // Fallback to static rates if fee provider fails
            return feeRateType switch
            {
                SweepConfiguration.FeeRates.Economy => new FeeRate(1.0m),
                SweepConfiguration.FeeRates.Normal => new FeeRate(5.0m),
                SweepConfiguration.FeeRates.Priority => new FeeRate(20.0m),
                _ => new FeeRate(5.0m)
            };
        }
    }
}
