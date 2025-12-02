using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.RockstarDev.Plugins.WalletHistoryReload.Services;

public class TransactionDataBackfillService
{
    private readonly MempoolSpaceApiService _mempoolApi;
    private readonly HistoricalPriceService _priceService;
    private readonly NBXplorerDbService _nbxService;
    private readonly ILogger<TransactionDataBackfillService> _logger;

    public TransactionDataBackfillService(
        MempoolSpaceApiService mempoolApi,
        HistoricalPriceService priceService,
        NBXplorerDbService nbxService,
        ILogger<TransactionDataBackfillService> logger)
    {
        _mempoolApi = mempoolApi;
        _priceService = priceService;
        _nbxService = nbxService;
        _logger = logger;
    }

    public async Task<BackfillResult> BackfillTransactionDataAsync(
        List<NBXTransactionData> transactions,
        string cryptoCode,
        bool includeFees = true,
        bool includeHistoricalPrices = true)
    {
        var result = new BackfillResult();

        try
        {
            _logger.LogInformation("Starting backfill for {Count} transactions", transactions.Count);

            foreach (var tx in transactions.Where(t => t.HasMissingData))
            {
                try
                {
                    // Fetch transaction data from Mempool.space
                    var txData = await _mempoolApi.GetTransactionDataAsync(tx.TransactionId);
                    
                    if (txData == null)
                    {
                        _logger.LogWarning("Failed to fetch data for transaction {TxId}", tx.TransactionId);
                        result.FailedTransactions++;
                        continue;
                    }

                    // Update NBXplorer database with fee data
                    if (includeFees && txData.Fee > 0)
                    {
                        await _nbxService.UpdateTransactionMetadataAsync(
                            tx.TransactionId, 
                            cryptoCode, 
                            txData.Fee, 
                            txData.FeeRate);
                    }

                    // Fetch historical price if requested
                    if (includeHistoricalPrices && txData.BlockTime > 0)
                    {
                        var timestamp = DateTimeOffset.FromUnixTimeSeconds(txData.BlockTime);
                        var btcPrice = await _priceService.GetHistoricalBtcPriceAsync(timestamp);
                        
                        if (btcPrice.HasValue)
                        {
                            _logger.LogInformation(
                                "Transaction {TxId}: BTC Price on {Date} was ${Price}",
                                tx.TransactionId, timestamp.ToString("yyyy-MM-dd"), btcPrice.Value);
                            
                            // TODO: Store historical price somewhere if needed
                        }
                    }

                    result.ProcessedTransactions++;
                    
                    // Rate limiting to avoid API throttling
                    await Task.Delay(500);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing transaction {TxId}", tx.TransactionId);
                    result.FailedTransactions++;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during backfill process");
            throw;
        }

        return result;
    }
}

public class BackfillResult
{
    public int ProcessedTransactions { get; set; }
    public int FailedTransactions { get; set; }
}
