using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.RockstarDev.Plugins.WalletHistoryReload.Services;
using BTCPayServer.RockstarDev.Plugins.WalletHistoryReload.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.RockstarDev.Plugins.WalletHistoryReload.Controllers;

[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanModifyStoreSettings)]
[Route("~/plugins/wallet-history-reload")]
public class WalletHistoryReloadController : Controller
{
    private readonly TransactionDataBackfillService _backfillService;
    private readonly NBXplorerDbService _nbxService;

    public WalletHistoryReloadController(
        TransactionDataBackfillService backfillService,
        NBXplorerDbService nbxService)
    {
        _backfillService = backfillService;
        _nbxService = nbxService;
    }

    [HttpGet("{storeId}/{cryptoCode}")]
    public async Task<IActionResult> Index(string storeId, string cryptoCode)
    {
        // Fetch transactions from NBXplorer database
        var nbxWalletId = GetNBXWalletId(storeId, cryptoCode);
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
        var nbxWalletId = GetNBXWalletId(storeId, cryptoCode);
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

    private string GetNBXWalletId(string storeId, string cryptoCode)
    {
        // NBXplorer wallet ID format is different from BTCPayServer
        // It's typically just the derivation scheme hash
        // For now, we'll need to look this up from BTCPayServer's store data
        // This is a simplified version - you may need to adjust based on your setup
        return storeId; // Placeholder - needs proper implementation
    }
}
