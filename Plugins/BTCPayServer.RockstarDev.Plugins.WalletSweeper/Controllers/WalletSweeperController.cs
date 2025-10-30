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
using Microsoft.Extensions.Logging;
using NBitcoin;

namespace BTCPayServer.RockstarDev.Plugins.WalletSweeper.Controllers;

[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Route("~/plugins/{storeId}/walletsweeper")]
public class WalletSweeperController(
    PluginDbContextFactory dbContextFactory,
    BTCPayNetworkProvider networkProvider,
    BTCPayWalletProvider walletProvider,
    PaymentMethodHandlerDictionary handlers,
    SeedEncryptionService seedEncryptionService,
    WalletSweeperService sweeperService)
    : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string storeId)
    {
        // Check if BTC wallet is configured
        var (balance, isHotWallet, hasWallet) = await GetWalletInfo(storeId);
        
        ViewBag.HasWallet = hasWallet;
        ViewBag.WalletBalance = balance;
        ViewBag.IsHotWallet = isHotWallet;

        await using var db = dbContextFactory.CreateContext();
        
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
        // Check if BTC wallet is configured
        var (balance, isHotWallet, hasWallet) = await GetWalletInfo(storeId);
        
        ViewBag.HasWallet = hasWallet;

        await using var db = dbContextFactory.CreateContext();
        
        var config = await db.SweepConfigurations
            .FirstOrDefaultAsync(c => c.StoreId == storeId);

        var viewModel = config != null 
            ? ConfigurationViewModel.FromModel(config) 
            : new ConfigurationViewModel();

        viewModel.IsHotWallet = isHotWallet;
        viewModel.WalletType = isHotWallet ? "Hot Wallet" : "Cold Wallet";
        viewModel.CurrentBalance = balance;

        return View(viewModel);
    }

    [HttpPost("configure")]
    public async Task<IActionResult> Save(string storeId, ConfigurationViewModel model)
    {
        // Get wallet info to check if it's cold wallet
        var (balance, isHotWallet, hasWallet) = await GetWalletInfo(storeId);
        model.IsHotWallet = isHotWallet;
        model.WalletType = isHotWallet ? "Hot Wallet" : "Cold Wallet";
        model.CurrentBalance = balance;
        
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
        
        // For cold wallets, require seed phrase if not already configured
        if (!isHotWallet && !model.HasEncryptedSeed && string.IsNullOrEmpty(model.SeedPhrase))
        {
            ModelState.AddModelError(nameof(model.SeedPhrase), "Seed phrase is required for cold wallets. Please provide your BIP39 mnemonic.");
        }

        if (!ModelState.IsValid)
        {
            ViewBag.HasWallet = hasWallet;
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
        if (!string.IsNullOrEmpty(model.SeedPhrase))
        {
            // Validate encryption password is provided
            if (string.IsNullOrEmpty(model.SeedPassword))
            {
                ModelState.AddModelError(nameof(model.SeedPassword), "Encryption password is required when providing a seed phrase.");
                ViewBag.HasWallet = hasWallet;
                return View("Configure", model);
            }
            
            // Validate password confirmation (only for new seed)
            if (!model.HasEncryptedSeed && model.SeedPassword != model.SeedPasswordConfirm)
            {
                ModelState.AddModelError(nameof(model.SeedPasswordConfirm), "Passwords do not match.");
                ViewBag.HasWallet = hasWallet;
                return View("Configure", model);
            }
            
            // Normalize seed phrase (remove extra whitespace, convert to lowercase)
            var normalizedSeed = string.Join(" ", model.SeedPhrase.Split(new[] { ' ', '\n', '\r', '\t' }, 
                StringSplitOptions.RemoveEmptyEntries))
                .Trim()
                .ToLowerInvariant();
            
            // Validate seed phrase format
            Mnemonic mnemonic;
            try
            {
                mnemonic = new Mnemonic(normalizedSeed, Wordlist.English);
                // Seed is valid - use normalized version
                model.SeedPhrase = normalizedSeed;
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(nameof(model.SeedPhrase), 
                    $"Invalid seed phrase format. Please provide a valid BIP39 mnemonic (12 or 24 words). Error: {ex.Message}");
                ViewBag.HasWallet = hasWallet;
                return View("Configure", model);
            }
            
            // Validate that seed matches the store's wallet
            try
            {
                var store = HttpContext.GetStoreData();
                var derivation = store?.GetDerivationSchemeSettings(handlers, "BTC");
                
                if (derivation == null)
                {
                    ModelState.AddModelError(nameof(model.SeedPhrase), "Could not retrieve wallet configuration.");
                    ViewBag.HasWallet = hasWallet;
                    return View("Configure", model);
                }
                
                var network = networkProvider.GetNetwork<BTCPayNetwork>("BTC");
                
                // Get the store's account key path and root fingerprint
                var storeAccountKey = derivation.AccountDerivation;
                var storeAccountKeyStr = storeAccountKey.ToString();
                
                // Derive master key from seed
                var masterKey = mnemonic.DeriveExtKey();
                var masterFingerprint = masterKey.Neuter().PubKey.GetHDFingerPrint();
                
                // Get the key path from the derivation scheme if available
                var accountKeyInfo = derivation.AccountKeySettings?.FirstOrDefault();
                bool matchFound = false;
                
                if (accountKeyInfo?.AccountKeyPath != null)
                {
                    // We have the full path - derive and compare
                    var derivedKey = masterKey.Derive(accountKeyInfo.AccountKeyPath);
                    var derivedPubKey = derivedKey.Neuter();
                    var derivedXpub = derivedPubKey.ToString(network.NBitcoinNetwork);
                    
                    // Check if derived xpub matches store's xpub
                    if (storeAccountKeyStr.Contains(derivedXpub) || 
                        storeAccountKeyStr.Contains(derivedPubKey.ToString(network.NBitcoinNetwork)))
                    {
                        matchFound = true;
                    }
                }
                
                // Fallback 1: Try to match root fingerprint
                if (!matchFound)
                {
                    var storeRootFingerprint = accountKeyInfo?.RootFingerprint;
                    if (storeRootFingerprint != null && masterFingerprint.Equals(storeRootFingerprint.Value))
                    {
                        matchFound = true;
                    }
                }
                
                // Fallback 2: Try common derivation paths if AccountKeySettings not available
                if (!matchFound)
                {
                    var commonPaths = new[]
                    {
                        "m/84'/1'/0'", // Native SegWit regtest
                        "m/84'/0'/0'", // Native SegWit mainnet
                        "m/49'/1'/0'", // SegWit regtest
                        "m/49'/0'/0'", // SegWit mainnet
                        "m/44'/1'/0'", // Legacy regtest
                        "m/44'/0'/0'"  // Legacy mainnet
                    };
                    
                    // Log for debugging
                    var logger = HttpContext.RequestServices.GetService(typeof(ILogger<WalletSweeperController>)) as ILogger<WalletSweeperController>;
                    logger?.LogInformation($"Store derivation scheme: {storeAccountKeyStr}");
                    
                    foreach (var path in commonPaths)
                    {
                        try
                        {
                            var derivedKey = masterKey.Derive(new KeyPath(path));
                            var derivedPubKey = derivedKey.Neuter();
                            var derivedXpub = derivedPubKey.ToString(network.NBitcoinNetwork);
                            
                            logger?.LogInformation($"Trying path {path}: {derivedXpub}");
                            
                            if (storeAccountKeyStr.Contains(derivedXpub))
                            {
                                matchFound = true;
                                logger?.LogInformation($"Match found at path {path}!");
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            logger?.LogWarning($"Error deriving path {path}: {ex.Message}");
                        }
                    }
                }
                
                if (!matchFound)
                {
                    ModelState.AddModelError(nameof(model.SeedPhrase), 
                        "The provided seed phrase does not match this store's Bitcoin wallet. Please verify you're using the correct seed phrase for this wallet.");
                    ViewBag.HasWallet = hasWallet;
                    return View("Configure", model);
                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(nameof(model.SeedPhrase), 
                    $"Failed to validate seed phrase against wallet: {ex.Message}");
                ViewBag.HasWallet = hasWallet;
                return View("Configure", model);
            }
            
            config.EncryptedSeed = seedEncryptionService.EncryptSeed(normalizedSeed, model.SeedPassword);
            config.SeedPassphrase = model.SeedPassword; // Store as hint (you might want to store just a hint, not the actual password)
        }

        await db.SaveChangesAsync();

        TempData[WellKnownTempData.SuccessMessage] = "Configuration saved successfully";
        return RedirectToAction(nameof(Index), new { storeId });
    }

    [HttpPost("trigger")]
    public async Task<IActionResult> TriggerManualSweep(string storeId, string seedPassword, CancellationToken cancellationToken)
    {
        try
        {
            var result = await sweeperService.TriggerManualSweep(storeId, seedPassword, cancellationToken);
            
            if (result.Success)
            {
                TempData[WellKnownTempData.SuccessMessage] = 
                    $"Manual sweep executed successfully! Transaction ID: {result.TxId}. " +
                    $"Amount: {result.Amount:N8} BTC, Fee: {result.Fee:N8} BTC. Check the history below for details.";
            }
            else
            {
                // Provide specific error messages based on result type
                switch (result.ResultType)
                {
                    case Services.SweepResult.SweepResultType.BelowMinimumThreshold:
                        TempData[WellKnownTempData.ErrorMessage] = result.ErrorMessage;
                        break;
                    case Services.SweepResult.SweepResultType.InsufficientBalance:
                        TempData[WellKnownTempData.ErrorMessage] = result.ErrorMessage;
                        break;
                    case Services.SweepResult.SweepResultType.NoConfiguration:
                        TempData[WellKnownTempData.ErrorMessage] = result.ErrorMessage;
                        break;
                    default:
                        TempData[WellKnownTempData.ErrorMessage] = $"Sweep failed: {result.ErrorMessage}";
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            TempData[WellKnownTempData.ErrorMessage] = $"Unexpected error during sweep: {ex.Message}";
        }

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

    private async Task<(decimal balance, bool isHotWallet, bool hasWallet)> GetWalletInfo(string storeId)
    {
        var store = HttpContext.GetStoreData();
        var derivation = store?.GetDerivationSchemeSettings(handlers, "BTC");
        
        if (derivation == null)
            return (0m, false, false); // No wallet configured

        var isHotWallet = derivation.IsHotWallet;
        var wallet = walletProvider.GetWallet("BTC");
        
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var balanceData = await wallet.GetBalance(derivation.AccountDerivation, cts.Token);
            var money = balanceData.Available ?? balanceData.Total;
            
            if (money is Money btcMoney)
            {
                return (btcMoney.ToDecimal(MoneyUnit.BTC), isHotWallet, true);
            }
        }
        catch
        {
            // Ignore errors, return 0 balance but wallet exists
        }

        return (0m, isHotWallet, true); // Wallet configured but balance fetch failed
    }

}
