using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
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
/// Also runs as a periodic task to check for automatic sweeps
/// </summary>
public class WalletSweeperService(
    PluginDbContextFactory dbContextFactory,
    StoreRepository storeRepository,
    BTCPayNetworkProvider networkProvider,
    ExplorerClientProvider explorerClientProvider,
    BTCPayWalletProvider walletProvider,
    PaymentMethodHandlerDictionary handlers,
    SeedEncryptionService seedEncryptionService,
    WalletRepository walletRepository,
    ILogger<WalletSweeperService> logger) : IPeriodicTask
{
    /// <summary>
    /// IPeriodicTask implementation - called periodically to check for automatic sweeps
    /// </summary>
    public async Task Do(CancellationToken cancellationToken)
    {
        await CheckAndSweepAll(cancellationToken);
    }
    /// <summary>
    /// Manually trigger a sweep for a specific configuration
    /// Password is retrieved from stored encrypted password
    /// </summary>
    public async Task<SweepResult> TriggerSweep(
        string configId,
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

        // Get the stored password
        if (string.IsNullOrEmpty(config.EncryptedPassword))
        {
            return SweepResult.Failure("No password stored for this configuration. Please recreate the configuration.");
        }

        return await ExecuteSweep(config, config.EncryptedPassword, "Manual", db, cancellationToken);
    }

    /// <summary>
    /// Check all enabled configurations and execute sweeps if needed
    /// </summary>
    public async Task CheckAndSweepAll(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("WalletSweeperService: Checking all configurations for automatic sweep triggers");

        await using var db = dbContextFactory.CreateContext();

        var configs = await db.SweepConfigurations
            .Where(c => c.AutoEnabled)
            .Include(c => c.TrackedUtxos.Where(u => !u.IsSpent))
            .ToListAsync(cancellationToken);

        logger.LogInformation($"WalletSweeperService: Found {configs.Count} auto-enabled configurations");

        foreach (var config in configs)
        {
            try
            {
                if (ShouldTriggerSweep(config, out var triggerType))
                {
                    logger.LogInformation($"WalletSweeperService: Triggering automatic {triggerType} sweep for {config.ConfigName}");
                    
                    // Get the stored password for auto-sweep
                    if (string.IsNullOrEmpty(config.EncryptedPassword))
                    {
                        logger.LogError($"WalletSweeperService: No password stored for {config.ConfigName} - cannot perform auto-sweep");
                        continue;
                    }
                    
                    // Execute automatic sweep with stored password
                    var result = await ExecuteSweep(config, config.EncryptedPassword, $"Automatic ({triggerType})", db, cancellationToken);
                    
                    if (result.IsSuccess)
                    {
                        logger.LogInformation($"WalletSweeperService: Automatic sweep succeeded for {config.ConfigName} - TxId: {result.TransactionId}");
                    }
                    else
                    {
                        logger.LogError($"WalletSweeperService: Automatic sweep failed for {config.ConfigName} - {result.ErrorMessage}");
                    }
                }
                else
                {
                    logger.LogDebug($"WalletSweeperService: No automatic sweep needed for {config.ConfigName}");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"WalletSweeperService: Error processing automatic sweep for {config.ConfigName}");
            }
        }
    }

    private bool ShouldTriggerSweep(SweepConfiguration config, out string triggerType)
    {
        triggerType = string.Empty;

        // Check 1: Balance below minimum
        if (config.CurrentBalance < config.MinimumBalance)
        {
            logger.LogDebug($"WalletSweeperService: {config.ConfigName} - Balance {config.CurrentBalance:N8} below minimum {config.MinimumBalance:N8}");
            return false;
        }

        // Check 2: Balance exceeds maximum - immediate sweep
        if (config.MaximumBalance > 0 && config.CurrentBalance >= config.MaximumBalance)
        {
            triggerType = "MaxThreshold";
            logger.LogInformation($"WalletSweeperService: {config.ConfigName} - Balance {config.CurrentBalance:N8} exceeds maximum {config.MaximumBalance:N8}");
            return true;
        }

        // Check 3: Scheduled sweep based on interval
        var intervalMinutes = config.IntervalMinutes > 0 ? config.IntervalMinutes : 60; // Default 60 minutes
        
        if (config.LastSwept.HasValue)
        {
            var minutesSinceLastSweep = (DateTimeOffset.UtcNow - config.LastSwept.Value).TotalMinutes;
            if (minutesSinceLastSweep >= intervalMinutes)
            {
                triggerType = "Scheduled";
                logger.LogInformation($"WalletSweeperService: {config.ConfigName} - {minutesSinceLastSweep:F1} minutes since last sweep (interval: {intervalMinutes} min)");
                return true;
            }
            else
            {
                logger.LogDebug($"WalletSweeperService: {config.ConfigName} - Only {minutesSinceLastSweep:F1} minutes since last sweep (interval: {intervalMinutes} min)");
            }
        }
        else
        {
            // Never swept before - trigger if above minimum
            triggerType = "Initial";
            logger.LogInformation($"WalletSweeperService: {config.ConfigName} - Never swept before, triggering initial sweep");
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
                
                // Reserve address (generatedBy is for tracking who generated it, not for labeling)
                var addressInfo = await wallet.ReserveAddressAsync(config.StoreId, derivationScheme.AccountDerivation, "walletsweeper");
                destinationAddress = addressInfo.Address;
                history.DestinationAddress = addressInfo.Address.ToString();
                
                // Add label to the address if AutoGenerateLabel is enabled
                if (config.AutoGenerateLabel)
                {
                    var walletId = new WalletId(config.StoreId, "BTC");
                    var addressObjectId = new WalletObjectId(walletId, WalletObjectData.Types.Address, addressInfo.Address.ToString());
                    var label = $"Sweep from {config.ConfigName}";
                    await walletRepository.AddWalletObjectLabels(addressObjectId, label);
                }
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
            logger.LogInformation($"WalletSweeperService: Using derivation strategy: {derivationStrategy}");
            logger.LogInformation($"WalletSweeperService: Derivation path: {config.DerivationPath}");
            
            var utxoChanges = await explorerClient.GetUTXOsAsync(derivationStrategy, cancellationToken);
            logger.LogInformation($"WalletSweeperService: NBXplorer returned {utxoChanges.Confirmed.UTXOs.Count} confirmed + {utxoChanges.Unconfirmed.UTXOs.Count} unconfirmed UTXOs");
            var allUtxos = utxoChanges.GetUnspentUTXOs().ToList();
            
            if (!allUtxos.Any())
            {
                throw new InvalidOperationException("No unspent UTXOs available to sweep");
            }
            
            // Calculate fee rate - FeeRate expects satoshis per kilobyte
            var feeRateSatPerVByte = config.FeeRate;
            var feeRate = new FeeRate(Money.Satoshis(feeRateSatPerVByte * 1000));
            
            // Calculate minimum viable UTXO value based on fee rate
            // A typical input costs ~68 vBytes to spend, so exclude UTXOs smaller than that cost
            var minUtxoValue = Money.Satoshis((long)(68 * feeRateSatPerVByte));
            
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
                // Estimate fee: inputs * 68 vB + outputs * 34 vB + 10 vB overhead
                var estimatedVBytes = (spendableUtxos.Count * 68) + (1 * 34) + 10;
                var estimatedFeeSats = estimatedVBytes * feeRateSatPerVByte;
                var estimatedFee = estimatedFeeSats / 100_000_000m;
                
                amountToSend = spendableBalance - config.ReserveAmount - estimatedFee;
                subtractFees = false;
                
                if (amountToSend <= 0)
                {
                    throw new InvalidOperationException($"Sweep amount is zero or negative after reserve and fees. Balance: {spendableBalance}, Reserve: {config.ReserveAmount}, Fee: {estimatedFee}");
                }
                
                logger.LogInformation($"WalletSweeperService: Reserve mode - sending {amountToSend:N8} BTC, keeping {config.ReserveAmount:N8} BTC as change, estimated fee: {estimatedFee:N8} BTC ({estimatedFeeSats} sats for {estimatedVBytes} vBytes)");
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
            logger.LogInformation($"WalletSweeperService: Including {spendableUtxos.Count} outpoints: {string.Join(", ", spendableUtxos.Select(u => $"{u.Outpoint.Hash}:{u.Outpoint.N}"))}");
            
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
            logger.LogInformation($"WalletSweeperService: Signing PSBT with {psbt.Inputs.Count} inputs");
            
            // Get the master fingerprint from the master key (not account key)
            var masterFingerprint = masterKey.GetPublicKey().GetHDFingerPrint();
            var rootedKeyPath = new RootedKeyPath(masterFingerprint, keyPath);
            
            // Rebase key paths to use the correct root
            psbt.RebaseKeyPaths(accountXpub, rootedKeyPath);
            
            // Sign each input with the derived key
            psbt.Settings.SigningOptions = new NBitcoin.SigningOptions { EnforceLowR = true };
            
            for (int i = 0; i < psbt.Inputs.Count; i++)
            {
                var input = psbt.Inputs[i];
                
                // Get the key path for this input from the PSBT
                var hdKeyPaths = input.HDKeyPaths;
                if (hdKeyPaths.Count == 0)
                {
                    logger.LogWarning($"WalletSweeperService: Input {i} has no HD key paths");
                    continue;
                }
                
                // Get the first key path (should only be one for single-sig)
                var keyPathInfo = hdKeyPaths.First();
                var fullKeyPath = keyPathInfo.Value.KeyPath;
                
                logger.LogInformation($"WalletSweeperService: Input {i} full key path: {fullKeyPath}");
                
                // The PSBT has the full path (e.g., 84'/1'/0'/0/70)
                // We need to derive relative to account key which is at m/84'/1'/0'
                // So we need to extract the relative path (0/70)
                // The account path is already in keyPath variable
                KeyPath relativeKeyPath;
                if (fullKeyPath.ToString().StartsWith(keyPath.ToString()))
                {
                    // Remove the account path prefix to get relative path
                    var accountPathLength = keyPath.Indexes.Length;
                    var relativeIndexes = fullKeyPath.Indexes.Skip(accountPathLength).ToArray();
                    relativeKeyPath = new KeyPath(relativeIndexes);
                }
                else
                {
                    // Fallback: assume it's already relative
                    relativeKeyPath = fullKeyPath;
                }
                
                logger.LogInformation($"WalletSweeperService: Input {i} relative key path: {relativeKeyPath}");
                
                // Derive the specific key for this input (relative to account key)
                var inputKey = accountKey.Derive(relativeKeyPath);
                
                // Sign this input with the private key
                input.Sign(inputKey.PrivateKey);
            }
            
            logger.LogInformation($"WalletSweeperService: All inputs signed");
            
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
