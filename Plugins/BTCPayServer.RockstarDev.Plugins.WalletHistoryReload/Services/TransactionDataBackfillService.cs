using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Services;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.RockstarDev.Plugins.WalletHistoryReload.Services;

public class TransactionDataBackfillService
{
    private readonly ILogger<TransactionDataBackfillService> _logger;
    private readonly MempoolSpaceApiService _mempoolApi;
    private readonly NBXplorerDbService _nbxService;
    private readonly HistoricalPriceService _priceService;
    private readonly WalletRepository _walletRepository;

    public TransactionDataBackfillService(
        MempoolSpaceApiService mempoolApi,
        HistoricalPriceService priceService,
        NBXplorerDbService nbxService,
        WalletRepository walletRepository,
        ILogger<TransactionDataBackfillService> logger)
    {
        _mempoolApi = mempoolApi;
        _priceService = priceService;
        _nbxService = nbxService;
        _walletRepository = walletRepository;
        _logger = logger;
    }

    public async Task<FetchResult> FetchTransactionDataAsync(
        List<NBXTransactionData> transactions,
        string storeId,
        string cryptoCode,
        string network = "mainnet",
        bool includeFees = true,
        bool includeHistoricalPrices = true)
    {
        var result = new FetchResult();
        var updatedTransactions = new List<NBXTransactionData>();

        try
        {
            _logger.LogInformation("Fetching data for {Count} transactions on {Network}", transactions.Count, network);

            foreach (var tx in transactions.Where(t => t.HasMissingData))
                try
                {
                    // Fetch transaction data from Mempool.space or Bitcoin Core (for regtest)
                    var txData = await _mempoolApi.GetTransactionDataAsync(tx.TransactionId, network, cryptoCode, tx.BlockHash);

                    if (txData == null)
                    {
                        _logger.LogWarning("Failed to fetch data for transaction {TxId}", tx.TransactionId);
                        result.FailedCount++;
                        continue;
                    }

                    // Update transaction object with fetched data (but don't save to DB yet)
                    if (includeFees && txData.Fee > 0 && !tx.Fee.HasValue)
                    {
                        tx.Fee = txData.Fee;
                        tx.FeeRate = txData.FeeRate;
                        tx.FeeWasFetched = true;
                    }

                    // Fetch historical price if requested
                    if (includeHistoricalPrices && txData.BlockTime > 0 && !tx.RateUsd.HasValue)
                    {
                        var btcPrice = await _priceService.GetHistoricalBtcPriceAsync(tx.Timestamp);

                        if (btcPrice.HasValue)
                        {
                            _logger.LogInformation(
                                "Transaction {TxId}: BTC Price on {Date} was ${Price}",
                                tx.TransactionId, tx.Timestamp.ToString("yyyy-MM-dd HH:00"), btcPrice.Value);

                            tx.RateUsd = btcPrice.Value;
                            tx.RateUsdWasFetched = true;
                        }
                    }

                    result.FetchedCount++;

                    // Rate limiting to avoid API throttling
                    await Task.Delay(500);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error fetching data for transaction {TxId}", tx.TransactionId);
                    result.FailedCount++;
                }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during fetch process");
            throw;
        }

        result.Transactions = transactions;
        return result;
    }

    public async Task<BackfillResult> SaveTransactionDataAsync(
        List<NBXTransactionData> transactions,
        string storeId,
        string cryptoCode,
        bool includeFees = true,
        bool includeHistoricalPrices = true)
    {
        var result = new BackfillResult();

        try
        {
            _logger.LogInformation("Saving fetched data for {Count} transactions", transactions.Count);

            foreach (var tx in transactions.Where(t => t.FeeWasFetched || t.RateUsdWasFetched))
                try
                {
                    // Save fee data to NBXplorer database
                    if (includeFees && tx.FeeWasFetched && tx.Fee.HasValue)
                        await _nbxService.UpdateTransactionMetadataAsync(
                            tx.TransactionId,
                            cryptoCode,
                            tx.Fee.Value,
                            tx.FeeRate ?? 0);

                    // Save historical price to BTCPayServer database
                    if (includeHistoricalPrices && tx.RateUsdWasFetched && tx.RateUsd.HasValue)
                        await SaveHistoricalRate(storeId, cryptoCode, tx.TransactionId, tx.RateUsd.Value);

                    result.ProcessedTransactions++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error saving data for transaction {TxId}", tx.TransactionId);
                    result.FailedTransactions++;
                }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during save process");
            throw;
        }

        return result;
    }

    public async Task<BackfillResult> BackfillTransactionDataAsync(
        List<NBXTransactionData> transactions,
        string storeId,
        string cryptoCode,
        string network = "mainnet",
        bool includeFees = true,
        bool includeHistoricalPrices = true)
    {
        var result = new BackfillResult();

        try
        {
            _logger.LogInformation("Starting backfill for {Count} transactions on {Network}", transactions.Count, network);

            foreach (var tx in transactions.Where(t => t.HasMissingData))
                try
                {
                    // Fetch transaction data from Mempool.space or Bitcoin Core (for regtest)
                    var txData = await _mempoolApi.GetTransactionDataAsync(tx.TransactionId, network, cryptoCode, tx.BlockHash);

                    if (txData == null)
                    {
                        _logger.LogWarning("Failed to fetch data for transaction {TxId}", tx.TransactionId);
                        result.FailedTransactions++;
                        continue;
                    }

                    // Update NBXplorer database with fee data
                    if (includeFees && txData.Fee > 0)
                        await _nbxService.UpdateTransactionMetadataAsync(
                            tx.TransactionId,
                            cryptoCode,
                            txData.Fee,
                            txData.FeeRate);

                    // Fetch and save historical price if requested
                    if (includeHistoricalPrices && txData.BlockTime > 0 && !tx.RateUsd.HasValue)
                    {
                        var timestamp = DateTimeOffset.FromUnixTimeSeconds(txData.BlockTime);
                        var btcPrice = await _priceService.GetHistoricalBtcPriceAsync(timestamp);

                        if (btcPrice.HasValue)
                        {
                            _logger.LogInformation(
                                "Transaction {TxId}: BTC Price on {Date} was ${Price}",
                                tx.TransactionId, timestamp.ToString("yyyy-MM-dd"), btcPrice.Value);

                            // Save the historical price to BTCPayServer database
                            await SaveHistoricalRate(storeId, cryptoCode, tx.TransactionId, btcPrice.Value);
                            tx.RateUsd = btcPrice.Value;
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during backfill process");
            throw;
        }

        return result;
    }

    private async Task SaveHistoricalRate(string storeId, string cryptoCode, string txId, decimal usdRate)
    {
        try
        {
            var walletId = new WalletId(storeId, cryptoCode);
            var walletObjectId = new WalletObjectId(walletId, WalletObjectData.Types.Tx, txId);

            // Get existing wallet object
            var walletObjects = await _walletRepository.GetWalletObjects(new GetWalletObjectsQuery
            {
                WalletId = walletId,
                Type = WalletObjectData.Types.Tx,
                Ids = new[] { txId }
            });

            if (walletObjects.TryGetValue(walletObjectId, out var existingObject))
            {
                // Parse existing data
                var data = string.IsNullOrEmpty(existingObject.Data)
                    ? new JObject()
                    : JObject.Parse(existingObject.Data);

                // Add or update the rates
                var rates = new JObject { ["USD"] = usdRate.ToString("F2") };
                data["rates"] = rates;

                // Update the wallet object
                await _walletRepository.SetWalletObject(walletObjectId, data);

                _logger.LogInformation("Saved USD rate ${Rate} for transaction {TxId}", usdRate, txId);
            }
            else
            {
                _logger.LogWarning("Wallet object not found for transaction {TxId}, cannot save rate", txId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving historical rate for transaction {TxId}", txId);
        }
    }
}

public class BackfillResult
{
    public int ProcessedTransactions { get; set; }
    public int FailedTransactions { get; set; }
}

public class FetchResult
{
    public List<NBXTransactionData> Transactions { get; set; } = new();
    public int FetchedCount { get; set; }
    public int FailedCount { get; set; }
}
