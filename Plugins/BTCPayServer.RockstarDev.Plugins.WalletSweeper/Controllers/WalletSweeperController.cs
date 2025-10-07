using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.RockstarDev.Plugins.WalletSweeper.Data;
using BTCPayServer.RockstarDev.Plugins.WalletSweeper.Data.Models;
using BTCPayServer.RockstarDev.Plugins.WalletSweeper.Services;
using BTCPayServer.RockstarDev.Plugins.WalletSweeper.ViewModels;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NBitcoin;

namespace BTCPayServer.RockstarDev.Plugins.WalletSweeper.Controllers;

[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Route("~/plugins/{storeId}/walletsweeper")]
public class WalletSweeperController(
    PluginDbContextFactory dbContextFactory,
    BTCPayNetworkProvider networkProvider,
    BTCPayWalletProvider walletProvider,
    PaymentMethodHandlerDictionary handlers)
    : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string storeId)
    {
        await using var db = dbContextFactory.CreateContext();
        
        var config = await db.SweepConfigurations
            .FirstOrDefaultAsync(c => c.StoreId == storeId);

        // Get sweep history
        var history = await db.SweepHistories
            .Where(h => h.StoreId == storeId)
            .OrderByDescending(h => h.Timestamp)
            .Take(20)
            .ToListAsync();

        // Get wallet info
        var (balance, isHotWallet) = await GetWalletInfo(storeId);

        ViewBag.History = history;
        ViewBag.Configuration = config;
        ViewBag.WalletBalance = balance;
        ViewBag.IsHotWallet = isHotWallet;

        return View();
    }

    [HttpGet("configure")]
    public async Task<IActionResult> Configure(string storeId)
    {
        await using var db = dbContextFactory.CreateContext();
        
        var config = await db.SweepConfigurations
            .FirstOrDefaultAsync(c => c.StoreId == storeId);

        var viewModel = config != null 
            ? ConfigurationViewModel.FromModel(config) 
            : new ConfigurationViewModel();

        // Get wallet info
        var (balance, isHotWallet) = await GetWalletInfo(storeId);
        viewModel.IsHotWallet = isHotWallet;
        viewModel.WalletType = isHotWallet ? "Hot Wallet" : "Cold Wallet";
        viewModel.CurrentBalance = balance;

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
                var network = networkProvider.GetNetwork<BTCPayNetwork>("BTC");
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

        await using var db = dbContextFactory.CreateContext();
        
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
        await using var db = dbContextFactory.CreateContext();
        
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

    private async Task<(decimal balance, bool isHotWallet)> GetWalletInfo(string storeId)
    {
        var store = HttpContext.GetStoreData();
        var derivation = store?.GetDerivationSchemeSettings(handlers, "BTC");
        
        if (derivation == null)
            return (0m, false);

        var isHotWallet = derivation.IsHotWallet;
        var wallet = walletProvider.GetWallet("BTC");
        
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var balanceData = await wallet.GetBalance(derivation.AccountDerivation, cts.Token);
            var money = balanceData.Available ?? balanceData.Total;
            
            if (money is Money btcMoney)
            {
                return (btcMoney.ToDecimal(MoneyUnit.BTC), isHotWallet);
            }
        }
        catch
        {
            // Ignore errors, return 0
        }

        return (0m, isHotWallet);
    }

}
