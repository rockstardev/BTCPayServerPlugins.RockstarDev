using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Services;
using Dapper;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.RockstarDev.Plugins.WalletHistoryReload.Services;

public class NBXplorerDbService
{
    private readonly NBXplorerConnectionFactory _connectionFactory;
    private readonly ILogger<NBXplorerDbService> _logger;

    public NBXplorerDbService(NBXplorerConnectionFactory connectionFactory, ILogger<NBXplorerDbService> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<List<NBXTransactionData>> GetWalletTransactionsAsync(string walletId, string cryptoCode)
    {
        var transactions = new List<NBXTransactionData>();

        try
        {
            if (!_connectionFactory.Available)
            {
                _logger.LogWarning("NBXplorer connection not available");
                return transactions;
            }

            await using var connection = await _connectionFactory.OpenConnection();

            var query = @"
                SELECT DISTINCT ON (r.tx_id)
                    r.tx_id,
                    r.seen_at,
                    r.balance_change / 100000000.0 as balance_change_btc,
                    t.blk_height IS NOT NULL as is_confirmed,
                    t.blk_height,
                    b.blk_id as block_hash,
                    CASE WHEN (t.metadata->'fees')::TEXT IS NOT NULL 
                         THEN (t.metadata->'fees')::BIGINT / 100000000.0 
                         ELSE NULL END as fee_btc,
                    (t.metadata->'feeRate')::NUMERIC as fee_rate
                FROM get_wallets_recent(
                    @walletId,
                    @cryptoCode, 
                    INTERVAL '1000 years',
                    NULL,
                    NULL
                ) r 
                JOIN txs t USING (code, tx_id)
                LEFT JOIN blks b ON (t.code = b.code AND t.blk_height = b.height)
                ORDER BY r.tx_id, r.seen_at DESC";

            var cmd = new CommandDefinition(
                query,
                new { walletId, cryptoCode });

            var rows = await connection.QueryAsync<dynamic>(cmd);

            foreach (var row in rows)
                transactions.Add(new NBXTransactionData
                {
                    TransactionId = row.tx_id,
                    Timestamp = row.seen_at,
                    BalanceChange = row.balance_change_btc,
                    IsConfirmed = row.is_confirmed,
                    BlockHeight = row.blk_height,
                    BlockHash = row.block_hash,
                    Fee = row.fee_btc,
                    FeeRate = row.fee_rate
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching transactions from NBXplorer database");
            throw;
        }

        return transactions;
    }

    public async Task UpdateTransactionMetadataAsync(string txId, string cryptoCode, decimal fee, decimal feeRate)
    {
        try
        {
            if (!_connectionFactory.Available)
            {
                _logger.LogWarning("NBXplorer connection not available");
                return;
            }

            await using var connection = await _connectionFactory.OpenConnection();

            // Update the metadata JSONB column with fees and feeRate
            var query = @"
                UPDATE txs 
                SET metadata = jsonb_set(
                    jsonb_set(
                        COALESCE(metadata, '{}'::jsonb),
                        '{fees}',
                        to_jsonb(@feeSatoshis::bigint)
                    ),
                    '{feeRate}',
                    to_jsonb(@feeRate::numeric)
                )
                WHERE code = @cryptoCode 
                AND tx_id = @txId";

            var cmd = new CommandDefinition(
                query,
                new
                {
                    txId,
                    cryptoCode,
                    feeSatoshis = (long)(fee * 100_000_000),
                    feeRate
                });

            var rowsAffected = await connection.ExecuteAsync(cmd);

            if (rowsAffected == 0)
                _logger.LogWarning("No transaction found to update: {TxId}", txId);
            else
                _logger.LogInformation("Updated transaction {TxId} with fee={Fee} BTC, feeRate={FeeRate} sat/vB",
                    txId, fee, feeRate);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating transaction metadata in NBXplorer database");
            throw;
        }
    }

    public async Task<int> ClearTransactionDataAsync(string walletId, string cryptoCode, int rowCount)
    {
        try
        {
            if (!_connectionFactory.Available)
            {
                _logger.LogWarning("NBXplorer connection not available");
                return 0;
            }

            await using var connection = await _connectionFactory.OpenConnection();

            // Get transaction IDs that have data to clear
            // Simply query the txs table directly for transactions with fee data
            var selectQuery = @"
                SELECT tx_id
                FROM txs
                WHERE code = @cryptoCode
                  AND ((metadata->'fees')::TEXT IS NOT NULL 
                       OR (metadata->'feeRate')::TEXT IS NOT NULL)
                ORDER BY tx_id
                LIMIT @rowCount";

            var txIds = await connection.QueryAsync<string>(
                selectQuery,
                new { cryptoCode, rowCount });

            if (!txIds.Any())
            {
                _logger.LogInformation("No transactions found with data to clear");
                return 0;
            }

            // Clear the fees and feeRate from metadata
            var updateQuery = @"
                UPDATE txs 
                SET metadata = CASE 
                    WHEN metadata IS NOT NULL THEN 
                        metadata - 'fees' - 'feeRate'
                    ELSE NULL 
                END
                WHERE code = @cryptoCode 
                AND tx_id = ANY(@txIds)";

            var clearedCount = await connection.ExecuteAsync(
                updateQuery,
                new { cryptoCode, txIds = txIds.ToArray() });

            _logger.LogInformation("Cleared data from {Count} transactions", clearedCount);
            return clearedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing transaction data from NBXplorer database");
            throw;
        }
    }
}

public class NBXTransactionData
{
    public string TransactionId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public decimal BalanceChange { get; set; }
    public bool IsConfirmed { get; set; }
    public long? BlockHeight { get; set; }
    public string? BlockHash { get; set; }
    public decimal? Fee { get; set; }
    public decimal? FeeRate { get; set; }
    public decimal? RateUsd { get; set; }

    // Flags to track what was newly fetched in this session
    public bool FeeWasFetched { get; set; }
    public bool RateUsdWasFetched { get; set; }

    public bool HasMissingData => !Fee.HasValue || !FeeRate.HasValue || !RateUsd.HasValue;
}
