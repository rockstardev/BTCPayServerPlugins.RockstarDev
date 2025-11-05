using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.RockstarDev.Plugins.WalletSweeper.Data;
using BTCPayServer.RockstarDev.Plugins.WalletSweeper.Data.Models;
using BTCPayServer.RockstarDev.Plugins.WalletSweeper.Services;
using BTCPayServer.RockstarDev.Plugins.WalletSweeper.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NBitcoin;

namespace BTCPayServer.RockstarDev.Plugins.WalletSweeper.Controllers;

[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Route("~/plugins/{storeId}/walletsweeper")]
public class WalletSweeperController(
    PluginDbContextFactory dbContextFactory,
    SeedEncryptionService seedEncryptionService,
    WalletSweeperService sweeperService) : Controller
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
    public IActionResult Create(string storeId)
    {
        var model = new ConfigurationViewModel();
        return View("Edit", model);
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
            return RedirectToAction(nameof(Index));
        }
        
        var model = ConfigurationViewModel.FromModel(config);
        return View(model);
    }
    
    [HttpPost("save")]
    public async Task<IActionResult> Save(string storeId, ConfigurationViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View("Edit", model);
        }
        
        // Validate seed phrase
        try
        {
            var mnemonic = new Mnemonic(model.SeedPhrase.Trim(), Wordlist.English);
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(nameof(model.SeedPhrase), $"Invalid seed phrase: {ex.Message}");
            return View("Edit", model);
        }
        
        await using var db = dbContextFactory.CreateContext();
        
        SweepConfiguration config;
        
        if (string.IsNullOrEmpty(model.Id))
        {
            // Create new
            config = new SweepConfiguration
            {
                Id = Guid.NewGuid().ToString(),
                StoreId = storeId,
                Created = DateTimeOffset.UtcNow
            };
            db.SweepConfigurations.Add(config);
        }
        else
        {
            // Update existing
            config = await db.SweepConfigurations
                .FirstOrDefaultAsync(c => c.Id == model.Id && c.StoreId == storeId);
            
            if (config == null)
            {
                TempData[WellKnownTempData.ErrorMessage] = "Configuration not found";
                return RedirectToAction(nameof(Index));
            }
        }
        
        // Update properties
        config.ConfigName = model.ConfigName;
        config.Description = model.Description;
        config.DerivationPath = model.DerivationPath;
        config.AddressGapLimit = model.AddressGapLimit;
        config.Enabled = model.Enabled;
        config.MinimumBalance = model.MinimumBalance;
        config.MaximumBalance = model.MaximumBalance;
        config.ReserveAmount = model.ReserveAmount;
        config.IntervalSeconds = model.IntervalSeconds;
        config.FeeRate = model.FeeRate;
        config.DestinationType = model.DestinationType;
        config.DestinationAddress = model.DestinationAddress;
        config.AutoGenerateLabel = model.AutoGenerateLabel;
        config.Updated = DateTimeOffset.UtcNow;
        
        // Encrypt and store seed phrase, derive xpub
        config.EncryptedSeed = seedEncryptionService.EncryptSeed(model.SeedPhrase.Trim(), model.SeedPassword);
        
        // Derive and store the account xpub for monitoring
        try
        {
            var mnemonic = new Mnemonic(model.SeedPhrase.Trim(), Wordlist.English);
            var masterKey = mnemonic.DeriveExtKey();
            var keyPath = new KeyPath(model.DerivationPath);
            var accountKey = masterKey.Derive(keyPath);
            config.AccountXpub = accountKey.Neuter().ToString(NBitcoin.Network.Main);
        }
        catch (Exception ex)
        {
            TempData[WellKnownTempData.ErrorMessage] = $"Failed to derive xpub: {ex.Message}";
            return View("Edit", model);
        }
        
        await db.SaveChangesAsync();
        
        TempData[WellKnownTempData.SuccessMessage] = "Configuration saved successfully";
        return RedirectToAction(nameof(Index));
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
            return RedirectToAction(nameof(Index));
        }
        
        db.SweepConfigurations.Remove(config);
        await db.SaveChangesAsync();
        
        TempData[WellKnownTempData.SuccessMessage] = "Configuration deleted successfully";
        return RedirectToAction(nameof(Index));
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
            return RedirectToAction(nameof(Index));
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
    public async Task<IActionResult> TriggerSweep(string storeId, string id, string seedPassword)
    {
        if (string.IsNullOrEmpty(seedPassword))
        {
            TempData[WellKnownTempData.ErrorMessage] = "Password is required to decrypt the seed phrase";
            return RedirectToAction(nameof(Details), new { storeId, id });
        }
        
        var result = await sweeperService.TriggerSweep(id, seedPassword);
        
        if (result.IsSuccess)
        {
            TempData[WellKnownTempData.SuccessMessage] = 
                $"Sweep executed successfully! TX: {result.TransactionId}, Amount: {result.Amount:N8} BTC, Fee: {result.Fee:N8} BTC";
        }
        else
        {
            TempData[WellKnownTempData.ErrorMessage] = $"Sweep failed: {result.ErrorMessage}";
        }
        
        return RedirectToAction(nameof(Details), new { storeId, id });
    }
}
