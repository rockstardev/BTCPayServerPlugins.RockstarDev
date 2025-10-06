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

namespace BTCPayServer.RockstarDev.Plugins.WalletSweeper.Controllers;

[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Route("~/plugins/{storeId}/walletsweeper")]
public class WalletSweeperController : Controller
{
    private readonly PluginDbContextFactory _dbContextFactory;

    public WalletSweeperController(PluginDbContextFactory dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string storeId)
    {
        await using var db = _dbContextFactory.CreateContext();
        
        var config = await db.SweepConfigurations
            .FirstOrDefaultAsync(c => c.StoreId == storeId);

        var viewModel = config != null 
            ? ConfigurationViewModel.FromModel(config) 
            : new ConfigurationViewModel();

        // TODO: Get wallet info (hot/cold, balance)
        viewModel.IsHotWallet = true; // Placeholder
        viewModel.WalletType = "Hot Wallet"; // Placeholder
        viewModel.CurrentBalance = 0.05m; // Placeholder

        // Get sweep history
        var history = await db.SweepHistories
            .Where(h => h.StoreId == storeId)
            .OrderByDescending(h => h.Timestamp)
            .Take(10)
            .ToListAsync();

        ViewBag.History = history;

        return View(viewModel);
    }

    [HttpPost]
    public async Task<IActionResult> Save(string storeId, ConfigurationViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View("Index", model);
        }

        await using var db = _dbContextFactory.CreateContext();
        
        var config = await db.SweepConfigurations
            .FirstOrDefaultAsync(c => c.StoreId == storeId);

        if (config == null)
        {
            config = model.ToModel(storeId);
            db.SweepConfigurations.Add(config);
        }
        else
        {
            // Update existing
            config.Enabled = model.Enabled;
            config.DestinationType = SweepConfiguration.DestinationTypes.Address;
            config.DestinationValue = model.DestinationAddress;
            config.MinimumBalance = model.MinimumBalance;
            config.MaximumBalance = model.MaximumBalance;
            config.ReserveAmount = model.ReserveAmount;
            config.IntervalDays = model.IntervalDays;
            config.FeeRate = Enum.Parse<SweepConfiguration.FeeRates>(model.FeeRate);
            config.Updated = DateTimeOffset.UtcNow;
        }

        // Handle seed phrase encryption if provided
        if (!string.IsNullOrEmpty(model.SeedPhrase) && !string.IsNullOrEmpty(model.SeedPassphrase))
        {
            config.EncryptedSeed = SeedEncryptionHelper.EncryptSeed(model.SeedPhrase, model.SeedPassphrase);
            config.SeedPassphrase = model.SeedPassphrase;
        }

        await db.SaveChangesAsync();

        TempData[WellKnownTempData.SuccessMessage] = "Configuration saved successfully";
        return RedirectToAction(nameof(Index), new { storeId });
    }

    [HttpPost("trigger")]
    public async Task<IActionResult> TriggerManualSweep(string storeId)
    {
        // TODO: Implement manual sweep trigger
        TempData[WellKnownTempData.SuccessMessage] = "Manual sweep triggered";
        return RedirectToAction(nameof(Index), new { storeId });
    }

}
