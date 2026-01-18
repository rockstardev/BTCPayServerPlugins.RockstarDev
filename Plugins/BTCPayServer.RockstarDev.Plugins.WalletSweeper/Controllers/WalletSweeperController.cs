using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.RockstarDev.Plugins.WalletSweeper.Data;
using BTCPayServer.RockstarDev.Plugins.WalletSweeper.Data.Models;
using BTCPayServer.RockstarDev.Plugins.WalletSweeper.Services;
using BTCPayServer.RockstarDev.Plugins.WalletSweeper.ViewModels;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NBitcoin;

namespace BTCPayServer.RockstarDev.Plugins.WalletSweeper.Controllers;

[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Route("~/plugins/{storeId}/walletsweeper")]
public class WalletSweeperController(
    PluginDbContextFactory dbContextFactory,
    SeedEncryptionService seedEncryptionService,
    WalletSweeperService sweeperService,
    UtxoMonitoringService utxoMonitoringService,
    BTCPayNetworkProvider networkProvider,
    ExplorerClientProvider explorerClientProvider,
    IFeeProviderFactory feeProviderFactory,
    ILogger<WalletSweeperController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string storeId)
    {
        await using var db = dbContextFactory.CreateContext();

        var configs = await db.SweepConfigurations
            .Where(c => c.StoreId == storeId)
            .OrderBy(c => c.ConfigName)
            .ToListAsync();

        return View(configs);
    }

    [HttpGet("create")]
    public async Task<IActionResult> Create(string storeId)
    {
        var network = networkProvider.GetNetwork<BTCPayNetwork>("BTC");
        var model = new CreateConfigurationViewModel();
        model.RecommendedSatoshiPerByte = (await GetRecommendedFees(network, feeProviderFactory)).Where(o => o != null).ToList()!;
        model.FeeBlockTarget = 1; // Default to 10 minutes
        return View(model);
    }

    [HttpPost("create")]
    public async Task<IActionResult> Create([FromRoute] string storeId, CreateConfigurationViewModel model)
    {
        // Additional validation for seed phrase format
        if (!string.IsNullOrEmpty(model.SeedPhrase))
            try
            {
                var mnemonic = new Mnemonic(model.SeedPhrase.Trim(), Wordlist.English);
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(nameof(model.SeedPhrase), $"Invalid seed phrase: {ex.Message}");
            }

        if (!ModelState.IsValid)
            return View(model);

        await using var db = dbContextFactory.CreateContext();

        var config = new SweepConfiguration
        {
            Id = Guid.NewGuid().ToString(),
            StoreId = storeId,
            Created = DateTimeOffset.UtcNow,
            DerivationPath = model.DerivationPath
        };

        // Encrypt seed and store password for auto-sweep functionality
        config.EncryptedSeed = seedEncryptionService.EncryptSeed(model.SeedPhrase.Trim(), model.SeedPassword);
        // Store password in plaintext - it's only useful with the encrypted seed
        config.EncryptedPassword = model.SeedPassword;

        var network = networkProvider.GetNetwork<BTCPayNetwork>("BTC");
        try
        {
            var mnemonic = new Mnemonic(model.SeedPhrase.Trim(), Wordlist.English);
            var masterKey = mnemonic.DeriveExtKey();
            var keyPath = new KeyPath(model.DerivationPath);
            var accountKey = masterKey.Derive(keyPath);
            config.AccountXpub = accountKey.Neuter().ToString(network.NBitcoinNetwork);

            logger.LogInformation($"Successfully derived xpub for config {model.ConfigName} on network {network.CryptoCode}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Failed to derive xpub for derivation path {model.DerivationPath}");
            ModelState.AddModelError("", $"Failed to derive account xpub. Please verify your seed phrase and derivation path. Error: {ex.Message}");
            return View(model);
        }

        // Set common properties
        MapCommonProperties(config, model);

        db.SweepConfigurations.Add(config);
        await db.SaveChangesAsync();

        // Track the derivation scheme in NBXplorer if not already tracked
        // This ensures NBXplorer monitors addresses and discovers UTXOs automatically
        try
        {
            var explorerClient = explorerClientProvider.GetExplorerClient("BTC");
            var derivationStrategy = network.NBXplorerNetwork.DerivationStrategyFactory.Parse(config.AccountXpub!);
            await explorerClient.TrackAsync(derivationStrategy);
            logger.LogInformation($"Tracked derivation scheme in NBXplorer for {model.ConfigName}");
        }
        catch (Exception ex)
        {
            // Non-fatal: NBXplorer might already be tracking this, or it will start on first query
            logger.LogWarning(ex, "Could not explicitly track derivation scheme in NBXplorer (may already be tracked)");
        }

        // Perform initial UTXO check immediately instead of waiting for next block/transaction event
        try
        {
            logger.LogInformation($"Performing initial UTXO check for {model.ConfigName}");
            await utxoMonitoringService.MonitorConfiguration(config, db, default);
            logger.LogInformation($"Initial UTXO check completed for {model.ConfigName} - Balance: {config.CurrentBalance:N8} BTC");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Error during initial UTXO check for {model.ConfigName}");
            // Non-fatal: Will be checked on next event
        }

        TempData[WellKnownTempData.SuccessMessage] = $"Configuration created successfully. Current balance: {config.CurrentBalance:N8} BTC";
        return RedirectToAction(nameof(Index), new { storeId });
    }

    [HttpGet("edit/{id}")]
    public async Task<IActionResult> Edit(string storeId, string id)
    {
        await using var db = dbContextFactory.CreateContext();

        var config = await db.SweepConfigurations
            .FirstOrDefaultAsync(c => c.Id == id && c.StoreId == storeId);

        if (config == null)
        {
            TempData[WellKnownTempData.ErrorMessage] = "Configuration not found";
            return RedirectToAction(nameof(Index), new { storeId });
        }

        var network = networkProvider.GetNetwork<BTCPayNetwork>("BTC");
        var model = EditConfigurationViewModel.FromModel(config);
        model.RecommendedSatoshiPerByte = (await GetRecommendedFees(network, feeProviderFactory)).Where(o => o != null).ToList()!;
        return View(model);
    }

    [HttpPost("edit/{id}")]
    public async Task<IActionResult> Edit([FromRoute] string storeId, string id, EditConfigurationViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        await using var db = dbContextFactory.CreateContext();

        var config = await db.SweepConfigurations
            .FirstOrDefaultAsync(c => c.Id == id && c.StoreId == storeId);

        if (config == null)
        {
            TempData[WellKnownTempData.ErrorMessage] = "Configuration not found";
            return RedirectToAction(nameof(Index), new { storeId });
        }

        // Update common properties
        MapCommonProperties(config, model);

        await db.SaveChangesAsync();

        TempData[WellKnownTempData.SuccessMessage] = "Configuration updated successfully";
        return RedirectToAction(nameof(Index), new { storeId });
    }

    private void MapCommonProperties(SweepConfiguration config, CreateConfigurationViewModel model)
    {
        config.ConfigName = model.ConfigName;
        config.Description = model.Description;
        config.AddressGapLimit = model.AddressGapLimit;
        config.AutoEnabled = model.AutoEnabled;
        config.MinimumBalance = model.MinimumBalance;
        config.MaximumBalance = model.MaximumBalance;
        config.ReserveAmount = model.ReserveAmount;
        config.IntervalMinutes = model.IntervalMinutes;
        config.FeeBlockTarget = model.FeeBlockTarget;
        config.DestinationType = model.DestinationType;
        config.DestinationAddress = model.DestinationAddress;
        config.AutoGenerateLabel = model.AutoGenerateLabel;
        config.Updated = DateTimeOffset.UtcNow;
    }

    private void MapCommonProperties(SweepConfiguration config, EditConfigurationViewModel model)
    {
        config.ConfigName = model.ConfigName;
        config.Description = model.Description;
        config.AddressGapLimit = model.AddressGapLimit;
        config.AutoEnabled = model.AutoEnabled;
        config.MinimumBalance = model.MinimumBalance;
        config.MaximumBalance = model.MaximumBalance;
        config.ReserveAmount = model.ReserveAmount;
        config.IntervalMinutes = model.IntervalMinutes;
        config.FeeBlockTarget = model.FeeBlockTarget;
        config.DestinationType = model.DestinationType;
        config.DestinationAddress = model.DestinationAddress;
        config.AutoGenerateLabel = model.AutoGenerateLabel;
        config.Updated = DateTimeOffset.UtcNow;
    }

    [HttpPost("delete/{id}")]
    public async Task<IActionResult> Delete(string storeId, string id)
    {
        await using var db = dbContextFactory.CreateContext();

        var config = await db.SweepConfigurations
            .Include(c => c.TrackedUtxos)
            .FirstOrDefaultAsync(c => c.Id == id && c.StoreId == storeId);

        if (config == null)
        {
            TempData[WellKnownTempData.ErrorMessage] = "Configuration not found";
            return RedirectToAction(nameof(Index), new { storeId });
        }

        db.SweepConfigurations.Remove(config);
        await db.SaveChangesAsync();

        TempData[WellKnownTempData.SuccessMessage] = "Configuration deleted successfully";
        return RedirectToAction(nameof(Index), new { storeId });
    }

    [HttpGet("details/{id}")]
    public async Task<IActionResult> Details(string storeId, string id)
    {
        await using var db = dbContextFactory.CreateContext();

        var config = await db.SweepConfigurations
            .Include(c => c.TrackedUtxos)
            .FirstOrDefaultAsync(c => c.Id == id && c.StoreId == storeId);

        if (config == null)
        {
            TempData[WellKnownTempData.ErrorMessage] = "Configuration not found";
            return RedirectToAction(nameof(Index), new { storeId });
        }

        // Get sweep history
        var history = await db.SweepHistories
            .Where(h => h.SweepConfigurationId == id)
            .OrderByDescending(h => h.SweepDate)
            .Take(20)
            .ToListAsync();

        ViewBag.History = history;
        return View(config);
    }

    [HttpPost("sweep/{id}")]
    public async Task<IActionResult> TriggerSweep(string storeId, string id)
    {
        var result = await sweeperService.TriggerSweep(id);

        if (result.IsSuccess)
            TempData[WellKnownTempData.SuccessMessage] =
                $"Sweep executed successfully! TX: {result.TransactionId}, Amount: {result.Amount:N8} BTC, Fee: {result.Fee:N8} BTC";
        else
            TempData[WellKnownTempData.ErrorMessage] = $"Sweep failed: {result.ErrorMessage}";

        return RedirectToAction(nameof(Details), new { storeId, id });
    }

    private static async Task<FeeRateOption?[]> GetRecommendedFees(BTCPayNetwork network, IFeeProviderFactory feeProviderFactory)
    {
        var feeProvider = feeProviderFactory.CreateFeeProvider(network);
        List<FeeRateOption?> options = new();
        foreach (var time in new[]
                 {
                     TimeSpan.FromMinutes(10.0), TimeSpan.FromMinutes(60.0), TimeSpan.FromHours(6.0),
                     TimeSpan.FromHours(24.0)
                 })
            try
            {
                var result = await feeProvider.GetFeeRateAsync((int)network.NBitcoinNetwork.Consensus.GetExpectedBlocksFor(time));
                options.Add(new FeeRateOption
                {
                    Target = time,
                    FeeRate = result.SatoshiPerByte
                });
            }
            catch (Exception)
            {
                options.Add(null);
            }

        return options.ToArray();
    }
}
