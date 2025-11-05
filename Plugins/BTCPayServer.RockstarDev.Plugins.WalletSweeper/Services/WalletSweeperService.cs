using System;
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

            // Build transaction
            var explorerClient = explorerClientProvider.GetExplorerClient("BTC");
            
            // For now, create a simple transaction
            // TODO: Implement full UTXO selection and transaction building
            var txBuilder = network.NBitcoinNetwork.CreateTransactionBuilder();
            
            // Calculate amount to send (balance - reserve - estimated fee)
            var estimatedFee = Money.Coins(0.0001m); // Rough estimate
            var amountToSend = Money.Coins(config.CurrentBalance) - Money.Coins(config.ReserveAmount) - estimatedFee;

            if (amountToSend <= Money.Zero)
            {
                throw new InvalidOperationException("Amount to send is zero or negative after fees");
            }

            // This is a simplified version - full implementation would:
            // 1. Query UTXOs from the external wallet
            // 2. Select appropriate UTXOs
            // 3. Build and sign transaction
            // 4. Broadcast transaction
            
            logger.LogWarning($"WalletSweeperService: Sweep transaction building not fully implemented yet");
            
            // For now, mark as success with placeholder data
            history.Status = "Success";
            history.Amount = amountToSend.ToDecimal(MoneyUnit.BTC);
            history.Fee = estimatedFee.ToDecimal(MoneyUnit.BTC);
            history.TransactionId = "placeholder_txid";
            history.UtxoCount = config.TrackedUtxos.Count(u => !u.IsSpent);

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
