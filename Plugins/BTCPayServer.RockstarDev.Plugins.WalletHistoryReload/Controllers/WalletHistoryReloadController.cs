using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Rating;
using BTCPayServer.RockstarDev.Plugins.WalletHistoryReload.Services;
using BTCPayServer.RockstarDev.Plugins.WalletHistoryReload.ViewModels;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace BTCPayServer.RockstarDev.Plugins.WalletHistoryReload.Controllers;

[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanModifyStoreSettings)]
[Route("~/plugins/wallet-history-reload")]
public class WalletHistoryReloadController : Controller
{
    private readonly TransactionDataBackfillService _backfillService;
    private readonly NBXplorerDbService _nbxService;
    private readonly StoreRepository _storeRepository;
    private readonly PaymentMethodHandlerDictionary _handlers;
    private readonly WalletRepository _walletRepository;
    private readonly IMemoryCache _cache;

    public WalletHistoryReloadController(
        TransactionDataBackfillService backfillService,
        NBXplorerDbService nbxService,
        StoreRepository storeRepository,
        PaymentMethodHandlerDictionary handlers,
        WalletRepository walletRepository,
        IMemoryCache cache)
    {
        _backfillService = backfillService;
        _nbxService = nbxService;
        _storeRepository = storeRepository;
        _handlers = handlers;
        _walletRepository = walletRepository;
        _cache = cache;
    }

    [HttpGet("{storeId}/{cryptoCode}")]
    public async Task<IActionResult> Index(string storeId, string cryptoCode)
    {
        // Fetch transactions from NBXplorer database
        var nbxWalletId = await GetNBXWalletId(storeId, cryptoCode);
        var transactions = await _nbxService.GetWalletTransactionsAsync(nbxWalletId, cryptoCode);

        // Enrich with USD rates from BTCPayServer database
        await EnrichWithUsdRates(transactions, storeId, cryptoCode);

        var network = GetNetworkFromCryptoCode(cryptoCode);

        var vm = new WalletHistoryReloadViewModel
        {
            StoreId = storeId,
            CryptoCode = cryptoCode,
            WalletId = $"S-{storeId}-{cryptoCode}",
            NBXWalletId = nbxWalletId,
            Network = network,
            Transactions = transactions,
            TotalTransactions = transactions.Count,
            MissingDataCount = transactions.Count(t => t.HasMissingData)
        };

        return View(vm);
    }

    [HttpPost("{storeId}/{cryptoCode}/preview")]
    public async Task<IActionResult> Preview(string storeId, string cryptoCode, WalletHistoryReloadViewModel vm)
    {
        // Fetch transactions again
        var nbxWalletId = await GetNBXWalletId(storeId, cryptoCode);
        var transactions = await _nbxService.GetWalletTransactionsAsync(nbxWalletId, cryptoCode);

        // Enrich with USD rates from BTCPayServer database
        await EnrichWithUsdRates(transactions, storeId, cryptoCode);

        // Detect network from crypto code
        var network = GetNetworkFromCryptoCode(cryptoCode);

        // Fetch data from APIs without saving to database
        var previewData = await _backfillService.FetchTransactionDataAsync(
            transactions,
            storeId,
            cryptoCode,
            network,
            vm.IncludeFees,
            vm.IncludeHistoricalPrices);

        vm.StoreId = storeId;
        vm.CryptoCode = cryptoCode;
        vm.WalletId = $"S-{storeId}-{cryptoCode}";
        vm.NBXWalletId = nbxWalletId;
        vm.Network = network;
        vm.Transactions = previewData.Transactions;
        vm.TotalTransactions = transactions.Count;
        vm.MissingDataCount = transactions.Count(t => t.HasMissingData);
        vm.FetchedDataCount = previewData.FetchedCount;
        vm.FetchFailedCount = previewData.FailedCount;

        // Store fetched transactions in memory cache with a unique key
        var cacheKey = Guid.NewGuid().ToString();
        _cache.Set(cacheKey, previewData.Transactions, TimeSpan.FromMinutes(30));
        vm.CacheKey = cacheKey;

        return View("Preview", vm);
    }

    [HttpPost("{storeId}/{cryptoCode}/confirm")]
    public async Task<IActionResult> Confirm(string storeId, string cryptoCode, WalletHistoryReloadViewModel vm)
    {
        // Retrieve fetched transactions from memory cache
        if (string.IsNullOrEmpty(vm.CacheKey))
        {
            TempData["ErrorMessage"] = "Invalid cache key. Please fetch the data again.";
            return RedirectToAction("Index", new { storeId, cryptoCode });
        }

        var fetchedTransactions = _cache.Get<List<NBXTransactionData>>(vm.CacheKey);
        
        if (fetchedTransactions == null)
        {
            TempData["ErrorMessage"] = "Preview data expired (30 min timeout). Please fetch the data again.";
            return RedirectToAction("Index", new { storeId, cryptoCode });
        }

        // Save the fetched data to database
        var result = await _backfillService.SaveTransactionDataAsync(
            fetchedTransactions,
            storeId,
            cryptoCode,
            vm.IncludeFees,
            vm.IncludeHistoricalPrices);

        // Clean up cache
        _cache.Remove(vm.CacheKey);

        // Prepare success view model
        vm.ProcessedTransactions = result.ProcessedTransactions;
        vm.FailedTransactions = result.FailedTransactions;
        vm.StoreId = storeId;
        vm.CryptoCode = cryptoCode;

        return View("Success", vm);
    }

    private async Task<string> GetNBXWalletId(string storeId, string cryptoCode)
    {
        var store = await _storeRepository.FindStore(storeId);
        if (store == null)
        {
            throw new InvalidOperationException($"Store {storeId} not found");
        }

        var derivationSettings = store.GetDerivationSchemeSettings(_handlers, cryptoCode);
        if (derivationSettings?.AccountDerivation == null)
        {
            throw new InvalidOperationException($"No derivation scheme configured for {cryptoCode} in store {storeId}");
        }

        // Use NBXplorer's utility to generate the wallet ID from the derivation scheme
        return NBXplorer.Client.DBUtils.nbxv1_get_wallet_id(cryptoCode, derivationSettings.AccountDerivation.ToString());
    }

    private async Task EnrichWithUsdRates(System.Collections.Generic.List<NBXTransactionData> transactions, string storeId, string cryptoCode)
    {
        var walletId = new WalletId(storeId, cryptoCode);
        var txIds = transactions.Select(t => t.TransactionId).ToArray();

        var walletObjects = await _walletRepository.GetWalletObjects(new GetWalletObjectsQuery
        {
            WalletId = walletId,
            Type = WalletObjectData.Types.Tx,
            Ids = txIds
        });

        foreach (var tx in transactions)
        {
            if (walletObjects.TryGetValue(new WalletObjectId(walletId, WalletObjectData.Types.Tx, tx.TransactionId), out var walletObject))
            {
                var rateBook = RateBook.FromTxWalletObject(walletObject);
                if (rateBook != null)
                {
                    var currencyPair = new CurrencyPair(cryptoCode, "USD");
                    var usdRate = rateBook.TryGetRate(currencyPair);
                    if (usdRate.HasValue)
                    {
                        tx.RateUsd = usdRate.Value;
                    }
                }
            }
        }
    }

    private string GetNetworkFromCryptoCode(string cryptoCode)
    {
        // Detect network based on crypto code suffix
        if (cryptoCode.EndsWith("-TESTNET", StringComparison.OrdinalIgnoreCase))
            return "testnet";
        if (cryptoCode.EndsWith("-SIGNET", StringComparison.OrdinalIgnoreCase))
            return "signet";
        if (cryptoCode.EndsWith("-REGTEST", StringComparison.OrdinalIgnoreCase))
            return "regtest";
        
        // For plain "BTC", check if it's actually regtest by looking at the network
        var store = _storeRepository.FindStore(HttpContext.GetStoreData()?.Id).GetAwaiter().GetResult();
        if (store != null)
        {
            var paymentMethodId = PaymentMethodId.Parse($"{cryptoCode}-CHAIN");
            var handler = _handlers.TryGet(paymentMethodId);
            if (handler is BitcoinLikePaymentHandler bitcoinHandler)
            {
                var network = bitcoinHandler.Network;
                var networkName = network.NBitcoinNetwork.Name.ToLowerInvariant();
                
                if (networkName.Contains("regtest"))
                    return "regtest";
                if (networkName.Contains("testnet"))
                    return "testnet";
                if (networkName.Contains("signet"))
                    return "signet";
            }
        }
        
        return "mainnet";
    }
}
