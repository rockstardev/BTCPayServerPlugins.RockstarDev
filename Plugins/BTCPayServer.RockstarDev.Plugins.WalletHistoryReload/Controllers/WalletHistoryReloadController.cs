using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.RockstarDev.Plugins.WalletHistoryReload.Services;
using BTCPayServer.RockstarDev.Plugins.WalletHistoryReload.ViewModels;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
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

    public WalletHistoryReloadController(
        TransactionDataBackfillService backfillService,
        NBXplorerDbService nbxService,
        StoreRepository storeRepository,
        PaymentMethodHandlerDictionary handlers)
    {
        _backfillService = backfillService;
        _nbxService = nbxService;
        _storeRepository = storeRepository;
        _handlers = handlers;
    }

    [HttpGet("{storeId}/{cryptoCode}")]
    public async Task<IActionResult> Index(string storeId, string cryptoCode)
    {
        // Fetch transactions from NBXplorer database
        var nbxWalletId = await GetNBXWalletId(storeId, cryptoCode);
        var transactions = await _nbxService.GetWalletTransactionsAsync(nbxWalletId, cryptoCode);

        var vm = new WalletHistoryReloadViewModel
        {
            StoreId = storeId,
            CryptoCode = cryptoCode,
            WalletId = $"S-{storeId}-{cryptoCode}",
            NBXWalletId = nbxWalletId,
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

        // Backfill only transactions with missing data
        var result = await _backfillService.BackfillTransactionDataAsync(
            transactions,
            cryptoCode,
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
}
