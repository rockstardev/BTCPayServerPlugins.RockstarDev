using System;
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

    public WalletHistoryReloadController(
        TransactionDataBackfillService backfillService,
        NBXplorerDbService nbxService,
        StoreRepository storeRepository,
        PaymentMethodHandlerDictionary handlers,
        WalletRepository walletRepository)
    {
        _backfillService = backfillService;
        _nbxService = nbxService;
        _storeRepository = storeRepository;
        _handlers = handlers;
        _walletRepository = walletRepository;
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

    [HttpPost("{storeId}/{cryptoCode}")]
    public async Task<IActionResult> Backfill(string storeId, string cryptoCode, WalletHistoryReloadViewModel vm)
    {
        // Fetch transactions again
        var nbxWalletId = await GetNBXWalletId(storeId, cryptoCode);
        var transactions = await _nbxService.GetWalletTransactionsAsync(nbxWalletId, cryptoCode);

        // Enrich with USD rates from BTCPayServer database
        await EnrichWithUsdRates(transactions, storeId, cryptoCode);

        // Detect network from crypto code
        var network = GetNetworkFromCryptoCode(cryptoCode);

        // Backfill only transactions with missing data
        var result = await _backfillService.BackfillTransactionDataAsync(
            transactions,
            storeId,
            cryptoCode,
            network,
            vm.IncludeFees,
            vm.IncludeHistoricalPrices);

        vm.ProcessedTransactions = result.ProcessedTransactions;
        vm.FailedTransactions = result.FailedTransactions;
        vm.BackfillCompleted = true;
        vm.Transactions = transactions;
        vm.TotalTransactions = transactions.Count;
        vm.MissingDataCount = transactions.Count(t => t.HasMissingData);

        TempData["SuccessMessage"] = $"Backfill completed: {result.ProcessedTransactions} processed, {result.FailedTransactions} failed";

        return View("Index", vm);
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
