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
using BTCPayServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NBitcoin;

namespace BTCPayServer.RockstarDev.Plugins.WalletSweeper.Controllers;

[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Route("~/plugins/{storeId}/walletsweeper")]
public class WalletSweeperController : Controller
{
    private readonly PluginDbContextFactory _dbContextFactory;
    private readonly BTCPayNetworkProvider _networkProvider;

    public WalletSweeperController(
        PluginDbContextFactory dbContextFactory,
        BTCPayNetworkProvider networkProvider)
    {
        _dbContextFactory = dbContextFactory;
        _networkProvider = networkProvider;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string storeId)
    {
        await using var db = _dbContextFactory.CreateContext();
        
        var config = await db.SweepConfigurations
            .FirstOrDefaultAsync(c => c.StoreId == storeId);

        // Get sweep history
        var history = await db.SweepHistories
            .Where(h => h.StoreId == storeId)
            .OrderByDescending(h => h.Timestamp)
            .Take(20)
            .ToListAsync();

        ViewBag.History = history;
        ViewBag.Configuration = config;

        return View();
    }

    [HttpGet("configure")]
    public async Task<IActionResult> Configure(string storeId)
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

        return View(viewModel);
    }

    [HttpPost("configure")]
    public async Task<IActionResult> Save(string storeId, ConfigurationViewModel model)
    {
        // Validate Bitcoin address
        if (!string.IsNullOrEmpty(model.DestinationAddress))
        {
            try
            {
                var network = _networkProvider.GetNetwork<BTCPayNetwork>("BTC");
                Network.Parse<BitcoinAddress>(model.DestinationAddress, network.NBitcoinNetwork);
            }
            catch (Exception)
            {
                ModelState.AddModelError(nameof(model.DestinationAddress), "Invalid Bitcoin address format.");
            }
        }

        if (!ModelState.IsValid)
        {
            return View("Configure", model);
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

    [HttpPost("delete")]
    public async Task<IActionResult> DeleteConfiguration(string storeId)
    {
        await using var db = _dbContextFactory.CreateContext();
        
        var config = await db.SweepConfigurations
            .FirstOrDefaultAsync(c => c.StoreId == storeId);

        if (config != null)
        {
            db.SweepConfigurations.Remove(config);
            await db.SaveChangesAsync();
            TempData[WellKnownTempData.SuccessMessage] = "Configuration deleted successfully";
        }

        return RedirectToAction(nameof(Index), new { storeId });
    }

}
