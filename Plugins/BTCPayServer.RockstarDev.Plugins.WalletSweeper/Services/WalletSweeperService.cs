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
    
    // Manual trigger
    

    /// <summary>
    /// Manually trigger a sweep for a specific store
    /// Note: Manual sweeps work regardless of the "Enabled" status (which controls automatic sweeps)
    /// </summary>
    public async Task<SweepResult> TriggerManualSweep(string storeId, CancellationToken cancellationToken = default)
    {
        logger.LogInformation($"WalletSweeper: Manual sweep triggered for store {storeId}");

        await using var db = dbContextFactory.CreateContext();

        var config = await db.SweepConfigurations
            .FirstOrDefaultAsync(c => c.StoreId == storeId, cancellationToken);

        if (config == null)
        {
            return SweepResult.FailureResult(
                $"No sweep configuration found for store {storeId}",
                SweepResult.SweepResultType.NoConfiguration);
        }

        // Get wallet balance
        var currentBalance = await GetWalletBalance(config.StoreId, cancellationToken);
        
        // Validate balance against minimum threshold
        if (currentBalance < config.MinimumBalance)
        {
            logger.LogWarning($"WalletSweeper: Balance {currentBalance} BTC is below minimum threshold {config.MinimumBalance} BTC");
            return SweepResult.FailureResult(
                $"Wallet balance ({currentBalance:N8} BTC) is below the minimum threshold ({config.MinimumBalance:N8} BTC). Sweep not executed.",
                SweepResult.SweepResultType.BelowMinimumThreshold);
        }
        
        // Check if balance is high enough to justify sweep after reserve and fees
        var minViableBalance = config.ReserveAmount + 0.0001m;
        if (currentBalance <= minViableBalance)
        {
            logger.LogWarning($"WalletSweeper: Balance {currentBalance} BTC is not high enough above reserve {config.ReserveAmount} BTC");
            return SweepResult.FailureResult(
                $"Wallet balance ({currentBalance:N8} BTC) is not high enough above the reserve amount ({config.ReserveAmount:N8} BTC) to justify a sweep.",
                SweepResult.SweepResultType.InsufficientBalance);
        }
        
        // Execute sweep with Manual trigger type
        return await ExecuteSweep(config, currentBalance, SweepHistory.TriggerTypes.Manual, cancellationToken);
    }
    
    // Utility methods

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

    private async Task<SweepResult> ExecuteSweep(
        SweepConfiguration config,
        decimal currentBalance,
        SweepHistory.TriggerTypes triggerType,
        CancellationToken cancellationToken)
    {
        logger.LogInformation($"WalletSweeper: Executing sweep for store {config.StoreId}, trigger: {triggerType}, balance: {currentBalance} BTC");

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
            
            // Determine if we're using SweepAll mode (when reserve is 0)
            var useSweepAll = config.ReserveAmount == 0;
            
            decimal sweepAmount = 0;
            decimal estimatedFee = 0;
            
            // Only calculate amounts if NOT using SweepAll (when reserve > 0)
            if (!useSweepAll)
            {
                // Estimate fee (rough estimate: ~200 vbytes for typical sweep transaction)
                estimatedFee = (feeRate.SatoshiPerByte * 200m) / 100_000_000m; // Convert sat to BTC

                // Calculate sweep amount: current balance - reserve amount - fee
                // This leaves exactly the reserve amount in the wallet after the sweep
                sweepAmount = currentBalance - config.ReserveAmount - estimatedFee;

                if (sweepAmount <= 0)
                {
                    var errorMsg = $"Sweep amount is zero or negative after accounting for reserve and fees. Balance: {currentBalance}, Reserve: {config.ReserveAmount}, Fee: {estimatedFee}";
                    logger.LogError($"WalletSweeper: {errorMsg}");
                    throw new InvalidOperationException(errorMsg);
                }

                logger.LogInformation(
                    $"WalletSweeper: Calculated sweep - Balance: {currentBalance} BTC, Reserve: {config.ReserveAmount} BTC, Fee: {estimatedFee} BTC, Sweep Amount: {sweepAmount} BTC");
            }
            else
            {
                logger.LogInformation(
                    $"WalletSweeper: Using SweepAll mode - Balance: {currentBalance} BTC, Reserve: {config.ReserveAmount} BTC");
            }

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

            // Calculate minimum viable UTXO value based on fee rate
            // A typical input costs ~68 vBytes to spend, so exclude UTXOs smaller than that cost
            var minUtxoValue = Money.Satoshis((long)(68 * feeRate.SatoshiPerByte));
            
            // Get actual UTXOs and calculate spendable balance (excluding dust)
            var utxos = await explorerClient.GetUTXOsAsync(derivation.AccountDerivation, cancellationToken);
            var allUtxos = utxos.GetUnspentUTXOs().ToList();
            var spendableUtxos = allUtxos.Where(u => ((Money)u.Value).CompareTo(minUtxoValue) >= 0).ToList();
            var dustUtxos = allUtxos.Where(u => ((Money)u.Value).CompareTo(minUtxoValue) < 0).ToList();
            var spendableBalance = spendableUtxos.Sum(u => ((Money)u.Value).ToDecimal(MoneyUnit.BTC));
            var dustBalance = dustUtxos.Sum(u => ((Money)u.Value).ToDecimal(MoneyUnit.BTC));

            logger.LogInformation($"WalletSweeper: Total UTXOs: {allUtxos.Count}, " +
                                $"Spendable: {spendableUtxos.Count} ({spendableBalance:N8} BTC), " +
                                $"Dust: {dustUtxos.Count} ({dustBalance:N8} BTC, threshold: {minUtxoValue.ToDecimal(MoneyUnit.BTC):N8} BTC)");

            if (spendableBalance == 0)
            {
                throw new InvalidOperationException($"No spendable UTXOs available. All UTXOs are below dust threshold of {minUtxoValue.ToDecimal(MoneyUnit.BTC):N8} BTC ({minUtxoValue.Satoshi} sats)");
            }

            // Determine how much to send based on mode
            decimal amountToSend;
            bool subtractFees;

            if (useSweepAll)
            {
                amountToSend = spendableBalance;
                subtractFees = true;
                logger.LogInformation($"WalletSweeper: SweepAll mode - sending {amountToSend:N8} BTC with fees subtracted");
            }
            else
            {
                amountToSend = sweepAmount;
                subtractFees = false;

                if (amountToSend <= 0)
                {
                    throw new InvalidOperationException($"Calculated sweep amount ({amountToSend:N8} BTC) is not positive after accounting for reserve and estimated fee");
                }

                if (amountToSend > spendableBalance)
                {
                    throw new InvalidOperationException($"Calculated sweep amount ({amountToSend:N8} BTC) exceeds spendable balance ({spendableBalance:N8} BTC)");
                }

                logger.LogInformation($"WalletSweeper: Reserve mode - sending {amountToSend:N8} BTC, keeping {config.ReserveAmount:N8} BTC as change (estimated fee {estimatedFee:N8} BTC)");
            }

            // Build PSBT request mirroring BTCPay Server send flow
            var createPSBTRequest = new CreatePSBTRequest
            {
                RBF = true,
                AlwaysIncludeNonWitnessUTXO = derivation.DefaultIncludeNonWitnessUtxo,
                IncludeGlobalXPub = derivation.IsMultiSigOnServer,
                IncludeOnlyOutpoints = spendableUtxos.Select(u => u.Outpoint).ToList(),
                Destinations = new List<CreatePSBTDestination>
                {
                    new CreatePSBTDestination
                    {
                        Destination = destinationAddress.ScriptPubKey,
                        Amount = useSweepAll ? null : Money.Coins(amountToSend),
                        SweepAll = useSweepAll,
                        SubstractFees = subtractFees
                    }
                },
                FeePreference = new FeePreference
                {
                    ExplicitFeeRate = feeRate
                }
            };

            logger.LogInformation($"WalletSweeper: Creating PSBT for sweep to {config.DestinationValue}, " +
                                $"mode: {(useSweepAll ? "SweepAll" : $"Amount: {amountToSend:N8} BTC")}, " +
                                $"reserve: {config.ReserveAmount} BTC");

            var psbtResponse = await explorerClient.CreatePSBTAsync(derivation.AccountDerivation, createPSBTRequest, cancellationToken);
            if (psbtResponse?.PSBT == null)
            {
                throw new InvalidOperationException("Failed to create PSBT - response was null");
            }
            var psbt = psbtResponse.PSBT;

            // Update actual amounts from PSBT (important when using SweepAll)
            var actualFeeFromPsbt = psbt.GetFee();
            history.Fee = actualFeeFromPsbt.ToDecimal(MoneyUnit.BTC);
            
            // Calculate actual sweep amount from outputs
            var actualSweepAmount = psbt.Outputs
                .Where(o => o.ScriptPubKey == destinationAddress.ScriptPubKey)
                .Sum(o => o.Value.ToDecimal(MoneyUnit.BTC));
            history.Amount = actualSweepAmount;
            
            logger.LogInformation($"WalletSweeper: PSBT created successfully. Actual sweep amount: {actualSweepAmount} BTC, Actual fee: {history.Fee} BTC");

            // Sign the PSBT
            logger.LogInformation($"WalletSweeper: Signing PSBT");
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
                throw new InvalidOperationException("Failed to sign PSBT - SignAll returned null");
            }
            logger.LogInformation($"WalletSweeper: PSBT signed successfully");

            // Finalize PSBT
            logger.LogInformation($"WalletSweeper: Finalizing PSBT");
            if (!psbt.TryFinalize(out var errors))
            {
                var errorDetails = errors.Count > 0 ? string.Join(", ", errors) : "Unknown error";
                throw new InvalidOperationException($"Failed to finalize PSBT: {errorDetails}");
            }
            logger.LogInformation($"WalletSweeper: PSBT finalized successfully");

            // Extract and broadcast transaction
            var transaction = psbt.ExtractTransaction();
            var txHash = transaction.GetHash().ToString();
            logger.LogInformation($"WalletSweeper: Broadcasting transaction {txHash}");

            var broadcastResult = await explorerClient.BroadcastAsync(transaction, cancellationToken);
            if (!broadcastResult.Success)
            {
                var broadcastError = $"Broadcast failed: {broadcastResult.RPCCode} {broadcastResult.RPCCodeMessage} {broadcastResult.RPCMessage}";
                logger.LogError($"WalletSweeper: {broadcastError}");
                throw new InvalidOperationException(broadcastError);
            }
            logger.LogInformation($"WalletSweeper: Transaction broadcast successful");

            // Update transaction ID and status
            history.TxId = txHash;
            history.Status = SweepHistory.SweepStatuses.Success;

            // Invalidate wallet cache
            var wallet = walletProvider.GetWallet("BTC");
            wallet.InvalidateCache(derivation.AccountDerivation);

            logger.LogInformation($"WalletSweeper: Sweep successful! TxId: {history.TxId}, Amount: {history.Amount} BTC, Fee: {history.Fee} BTC");

            // Update last sweep date
            config.LastSweepDate = DateTimeOffset.UtcNow;
            db.SweepConfigurations.Update(config);
            
            db.SweepHistories.Add(history);
            await db.SaveChangesAsync(cancellationToken);
            
            return SweepResult.SuccessResult(history.TxId, history.Amount, history.Fee);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"WalletSweeper: Sweep failed for store {config.StoreId}: {ex.Message}");
            
            // Only save history for automatic/scheduled sweeps, not manual triggers
            // Manual trigger failures are shown as UI notifications only
            if (triggerType != SweepHistory.TriggerTypes.Manual)
            {
                history.Status = SweepHistory.SweepStatuses.Failed;
                history.ErrorMessage = ex.Message;
                
                db.SweepHistories.Add(history);
                await db.SaveChangesAsync(cancellationToken);
            }
            
            return SweepResult.FailureResult(ex.Message);
        }
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
