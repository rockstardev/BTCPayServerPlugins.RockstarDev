using System.Collections.Generic;
using BTCPayServer.RockstarDev.Plugins.WalletHistoryReload.Services;

namespace BTCPayServer.RockstarDev.Plugins.WalletHistoryReload.ViewModels;

public class WalletHistoryReloadViewModel
{
    public string StoreId { get; set; } = string.Empty;
    public string CryptoCode { get; set; } = string.Empty;
    public string WalletId { get; set; } = string.Empty;
    public string NBXWalletId { get; set; } = string.Empty;
    public string Network { get; set; } = "mainnet";
    
    public List<NBXTransactionData> Transactions { get; set; } = new();
    public int TotalTransactions { get; set; }
    public int MissingDataCount { get; set; }
    
    public bool IncludeFees { get; set; } = true;
    public bool IncludeHistoricalPrices { get; set; } = true;
    
    public bool BackfillCompleted { get; set; }
    public int ProcessedTransactions { get; set; }
    public int FailedTransactions { get; set; }
    
    // Preview data
    public int FetchedDataCount { get; set; }
    public int FetchFailedCount { get; set; }
    public string CacheKey { get; set; } = string.Empty;
}
