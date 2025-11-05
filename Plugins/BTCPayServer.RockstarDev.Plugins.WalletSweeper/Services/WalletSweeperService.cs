using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.RockstarDev.Plugins.WalletSweeper.Data;
using BTCPayServer.RockstarDev.Plugins.WalletSweeper.Data.Models;
using BTCPayServer.Services;
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

/// <summary>
/// Service for executing sweeps from external wallets to the central store
/// </summary>
public class WalletSweeperService(
    PluginDbContextFactory dbContextFactory,
    StoreRepository storeRepository,
    BTCPayNetworkProvider networkProvider,
    ExplorerClientProvider explorerClientProvider,
    BTCPayWalletProvider walletProvider,
    PaymentMethodHandlerDictionary handlers,
    SeedEncryptionService seedEncryptionService,
    ILogger<WalletSweeperService> logger)
{
    /// <summary>
    /// Manually trigger a sweep for a specific configuration
    /// </summary>
    public async Task<SweepResult> TriggerSweep(
        string configId,
        string seedPassword,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation($"WalletSweeperService: Manual sweep triggered for config {configId}");

        await using var db = dbContextFactory.CreateContext();

        var config = await db.SweepConfigurations
            .Include(c => c.TrackedUtxos.Where(u => !u.IsSpent))
            .FirstOrDefaultAsync(c => c.Id == configId, cancellationToken);

        if (config == null)
        {
            return SweepResult.Failure("Configuration not found");
        }

        // Check balance threshold
        if (config.CurrentBalance < config.MinimumBalance)
        {
            return SweepResult.Failure($"Balance ({config.CurrentBalance:N8} BTC) is below minimum threshold ({config.MinimumBalance:N8} BTC)");
        }

        return await ExecuteSweep(config, seedPassword, "Manual", db, cancellationToken);
    }

    /// <summary>
    /// Check all enabled configurations and execute sweeps if needed
    /// </summary>
    public async Task CheckAndSweepAll(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("WalletSweeperService: Checking all configurations for sweep triggers");

        await using var db = dbContextFactory.CreateContext();

        var configs = await db.SweepConfigurations
            .Where(c => c.Enabled)
            .Include(c => c.TrackedUtxos.Where(u => !u.IsSpent))
            .ToListAsync(cancellationToken);

        foreach (var config in configs)
        {
            try
            {
                if (ShouldTriggerSweep(config, out var triggerType))
                {
                    logger.LogInformation($"WalletSweeperService: Triggering {triggerType} sweep for {config.ConfigName}");
                    
                    // For automatic sweeps, we can't decrypt the seed without password
                    // This is a limitation - automatic sweeps require the seed to be accessible
                    // For now, we'll skip automatic sweeps
                    logger.LogWarning($"WalletSweeperService: Automatic sweeps not yet implemented (requires password-less seed access)");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"WalletSweeperService: Error checking config {config.ConfigName}");
            }
        }
    }

    private bool ShouldTriggerSweep(SweepConfiguration config, out string triggerType)
    {
        triggerType = string.Empty;

        // Check 1: Balance below minimum
        if (config.CurrentBalance < config.MinimumBalance)
        {
            return false;
        }

        // Check 2: Balance exceeds maximum - immediate sweep
        if (config.CurrentBalance >= config.MaximumBalance)
        {
            triggerType = "MaxThreshold";
            return true;
        }

        // Check 3: Scheduled sweep based on interval
        if (config.LastSwept.HasValue)
        {
            var secondsSinceLastSweep = (DateTimeOffset.UtcNow - config.LastSwept.Value).TotalSeconds;
            if (secondsSinceLastSweep >= config.IntervalSeconds)
            {
                triggerType = "Scheduled";
                return true;
            }
        }
        else
        {
            // Never swept before - trigger if above minimum
            triggerType = "Initial";
            return true;
        }

        return false;
    }

    private async Task<SweepResult> ExecuteSweep(
        SweepConfiguration config,
        string seedPassword,
        string triggerType,
        PluginDbContext db,
        CancellationToken cancellationToken)
    {
        logger.LogInformation($"WalletSweeperService: Executing sweep for {config.ConfigName}");

        // Create history record
        var history = new SweepHistory
        {
            Id = Guid.NewGuid().ToString(),
            SweepConfigurationId = config.Id,
            SweepDate = DateTimeOffset.UtcNow,
            Status = "Pending",
            TriggerType = triggerType
        };

        try
        {
            // Validate configuration
            if (string.IsNullOrEmpty(config.AccountXpub))
            {
                throw new InvalidOperationException("Configuration is missing account xpub. Please reconfigure the sweep settings.");
            }
            
            if (string.IsNullOrEmpty(config.DerivationPath))
            {
                throw new InvalidOperationException("Configuration is missing derivation path. Please reconfigure the sweep settings.");
            }
            
            // Decrypt seed
            string seedPhrase;
            try
            {
                seedPhrase = seedEncryptionService.DecryptSeed(config.EncryptedSeed!, seedPassword);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to decrypt seed phrase. Check your password.", ex);
            }

            var network = networkProvider.GetNetwork<BTCPayNetwork>("BTC");
            var mnemonic = new Mnemonic(seedPhrase, Wordlist.English);
            var masterKey = mnemonic.DeriveExtKey();

            // Derive account key
            var keyPath = new KeyPath(config.DerivationPath!);
            var accountKey = masterKey.Derive(keyPath);
            var accountXpub = accountKey.Neuter();

            // Get destination address
            BitcoinAddress destinationAddress;
            if (config.DestinationType == DestinationType.ThisStore)
            {
                // Get store and generate new address
                var store = await storeRepository.FindStore(config.StoreId);
                if (store == null)
                {
                    throw new InvalidOperationException("Store not found");
                }

                var derivationScheme = store.GetDerivationSchemeSettings(handlers, "BTC");
                if (derivationScheme == null)
                {
                    throw new InvalidOperationException("Store has no BTC wallet configured");
                }

                var wallet = walletProvider.GetWallet("BTC");
                var label = config.AutoGenerateLabel ? $"Sweep from {config.ConfigName}" : null;
                var addressInfo = await wallet.ReserveAddressAsync(config.StoreId, derivationScheme.AccountDerivation, label);
                destinationAddress = addressInfo.Address;
                history.DestinationAddress = addressInfo.Address.ToString();
            }
            else
            {
                // Use custom address
                if (string.IsNullOrEmpty(config.DestinationAddress))
                {
                    throw new InvalidOperationException("Destination address not configured");
                }
                destinationAddress = BitcoinAddress.Create(config.DestinationAddress, network.NBitcoinNetwork);
                history.DestinationAddress = config.DestinationAddress;
            }

            // Build and broadcast transaction using PSBT approach
            var explorerClient = explorerClientProvider.GetExplorerClient("BTC");
            
            // Get actual UTXOs from NBXplorer (the source of truth)
            var derivationStrategy = network.NBXplorerNetwork.DerivationStrategyFactory.Parse(config.AccountXpub!);
            var utxoChanges = await explorerClient.GetUTXOsAsync(derivationStrategy, cancellationToken);
            var allUtxos = utxoChanges.GetUnspentUTXOs().ToList();
            
            if (!allUtxos.Any())
            {
                throw new InvalidOperationException("No unspent UTXOs available to sweep");
            }
            
            // Calculate minimum viable UTXO value based on fee rate
            // A typical input costs ~68 vBytes to spend, so exclude UTXOs smaller than that cost
            var feeRate = new FeeRate(Money.Satoshis(config.FeeRate));
            var minUtxoValue = Money.Satoshis((long)(68 * feeRate.SatoshiPerByte));
            
            var spendableUtxos = allUtxos.Where(u => ((Money)u.Value).CompareTo(minUtxoValue) >= 0).ToList();
            var dustUtxos = allUtxos.Where(u => ((Money)u.Value).CompareTo(minUtxoValue) < 0).ToList();
            var spendableBalance = spendableUtxos.Sum(u => ((Money)u.Value).ToDecimal(MoneyUnit.BTC));
            var dustBalance = dustUtxos.Sum(u => ((Money)u.Value).ToDecimal(MoneyUnit.BTC));
            
            logger.LogInformation($"WalletSweeperService: Total UTXOs: {allUtxos.Count}, " +
                                $"Spendable: {spendableUtxos.Count} ({spendableBalance:N8} BTC), " +
                                $"Dust: {dustUtxos.Count} ({dustBalance:N8} BTC, threshold: {minUtxoValue.ToDecimal(MoneyUnit.BTC):N8} BTC)");
            
            if (!spendableUtxos.Any())
            {
                throw new InvalidOperationException($"No spendable UTXOs available. All UTXOs are below dust threshold of {minUtxoValue.ToDecimal(MoneyUnit.BTC):N8} BTC ({minUtxoValue.Satoshi} sats)");
            }
            
            // Determine if we're using SweepAll mode (when reserve is 0)
            var useSweepAll = config.ReserveAmount == 0;
            decimal amountToSend;
            bool subtractFees;
            
            if (useSweepAll)
            {
                amountToSend = spendableBalance;
                subtractFees = true;
                logger.LogInformation($"WalletSweeperService: SweepAll mode - sending {amountToSend:N8} BTC with fees subtracted");
            }
            else
            {
                // Estimate fee (rough: ~200 vbytes for typical sweep)
                var estimatedFee = (feeRate.SatoshiPerByte * 200m) / 100_000_000m;
                amountToSend = spendableBalance - config.ReserveAmount - estimatedFee;
                subtractFees = false;
                
                if (amountToSend <= 0)
                {
                    throw new InvalidOperationException($"Sweep amount is zero or negative after reserve and fees. Balance: {spendableBalance}, Reserve: {config.ReserveAmount}, Fee: {estimatedFee}");
                }
                
                logger.LogInformation($"WalletSweeperService: Reserve mode - sending {amountToSend:N8} BTC, keeping {config.ReserveAmount:N8} BTC as change");
            }
            
            // Build PSBT request
            var createPSBTRequest = new CreatePSBTRequest
            {
                RBF = true,
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
            
            logger.LogInformation($"WalletSweeperService: Creating PSBT for sweep to {destinationAddress}");
            
            var psbtResponse = await explorerClient.CreatePSBTAsync(derivationStrategy, createPSBTRequest, cancellationToken);
            if (psbtResponse?.PSBT == null)
            {
                throw new InvalidOperationException("Failed to create PSBT - response was null");
            }
            var psbt = psbtResponse.PSBT;
            
            // Update actual amounts from PSBT
            var actualFeeFromPsbt = psbt.GetFee();
            history.Fee = actualFeeFromPsbt.ToDecimal(MoneyUnit.BTC);
            
            var actualSweepAmount = psbt.Outputs
                .Where(o => o.ScriptPubKey == destinationAddress.ScriptPubKey)
                .Sum(o => o.Value.ToDecimal(MoneyUnit.BTC));
            history.Amount = actualSweepAmount;
            
            logger.LogInformation($"WalletSweeperService: PSBT created. Amount: {actualSweepAmount:N8} BTC, Fee: {history.Fee:N8} BTC");
            
            // Sign the PSBT
            logger.LogInformation($"WalletSweeperService: Signing PSBT");
            var rootedKeyPath = new RootedKeyPath(accountKey.GetPublicKey().GetHDFingerPrint(), new KeyPath(config.DerivationPath!));
            
            psbt.RebaseKeyPaths(accountXpub, rootedKeyPath);
            psbt.Settings.SigningOptions = new NBitcoin.SigningOptions { EnforceLowR = true };
            var signed = psbt.SignAll(derivationStrategy, accountKey, rootedKeyPath);
            
            if (signed == null)
            {
                throw new InvalidOperationException("Failed to sign PSBT");
            }
            logger.LogInformation($"WalletSweeperService: PSBT signed successfully");
            
            // Finalize PSBT
            logger.LogInformation($"WalletSweeperService: Finalizing PSBT");
            if (!psbt.TryFinalize(out var errors))
            {
                var errorDetails = errors.Count > 0 ? string.Join(", ", errors) : "Unknown error";
                throw new InvalidOperationException($"Failed to finalize PSBT: {errorDetails}");
            }
            logger.LogInformation($"WalletSweeperService: PSBT finalized successfully");
            
            // Extract and broadcast transaction
            var tx = psbt.ExtractTransaction();
            var txHash = tx.GetHash().ToString();
            logger.LogInformation($"WalletSweeperService: Broadcasting transaction {txHash}");
            
            var broadcastResult = await explorerClient.BroadcastAsync(tx, cancellationToken);
            if (!broadcastResult.Success)
            {
                throw new InvalidOperationException($"Broadcast failed: {broadcastResult.RPCCode} {broadcastResult.RPCCodeMessage} {broadcastResult.RPCMessage}");
            }
            logger.LogInformation($"WalletSweeperService: Transaction broadcast successful");
            
            // Update history
            history.Status = "Success";
            history.TransactionId = txHash;
            history.UtxoCount = spendableUtxos.Count;
            
            // Mark our tracked UTXOs as spent
            var spentOutpoints = spendableUtxos.Select(u => $"{u.Outpoint.Hash}:{u.Outpoint.N}").ToHashSet();
            var trackedUtxos = config.TrackedUtxos.Where(u => !u.IsSpent && spentOutpoints.Contains(u.Outpoint)).ToList();
            
            foreach (var utxo in trackedUtxos)
            {
                utxo.IsSpent = true;
                utxo.SpentDate = DateTimeOffset.UtcNow;
                utxo.SpentInSweepTxId = txHash;
            }
            
            logger.LogInformation($"WalletSweeperService: Marked {trackedUtxos.Count} tracked UTXOs as spent");

            // Update config
            config.LastSwept = DateTimeOffset.UtcNow;
            config.CurrentBalance = config.ReserveAmount;

            db.SweepHistories.Add(history);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation($"WalletSweeperService: Sweep completed for {config.ConfigName}");
            return SweepResult.Success(history.TransactionId, history.Amount, history.Fee);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"WalletSweeperService: Sweep failed for {config.ConfigName}");
            
            history.Status = "Failed";
            history.ErrorMessage = ex.Message;
            
            db.SweepHistories.Add(history);
            await db.SaveChangesAsync(cancellationToken);

            return SweepResult.Failure(ex.Message);
        }
    }
}

public class SweepResult
{
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public string? TransactionId { get; set; }
    public decimal Amount { get; set; }
    public decimal Fee { get; set; }

    public static SweepResult Success(string txId, decimal amount, decimal fee)
    {
        return new SweepResult
        {
            IsSuccess = true,
            TransactionId = txId,
            Amount = amount,
            Fee = fee
        };
    }

    public static SweepResult Failure(string error)
    {
        return new SweepResult
        {
            IsSuccess = false,
            ErrorMessage = error
        };
    }
}
