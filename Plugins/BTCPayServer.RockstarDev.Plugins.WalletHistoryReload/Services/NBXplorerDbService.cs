using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BTCPayServer.RockstarDev.Plugins.WalletHistoryReload.Services;

public class NBXplorerDbService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<NBXplorerDbService> _logger;

    public NBXplorerDbService(IConfiguration configuration, ILogger<NBXplorerDbService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    private string GetNBXplorerConnectionString()
    {
        // NBXplorer uses the same Postgres connection as BTCPayServer but different database
        var btcpayConnectionString = _configuration.GetConnectionString("DefaultConnection");
        
        if (string.IsNullOrEmpty(btcpayConnectionString))
        {
            throw new InvalidOperationException("BTCPayServer connection string not found");
        }

        // Replace database name with NBXplorer database
        var builder = new NpgsqlConnectionStringBuilder(btcpayConnectionString);
        builder.Database = "nbxplorer"; // NBXplorer default database name
        
        return builder.ToString();
    }

    public async Task<List<NBXTransactionData>> GetWalletTransactionsAsync(string walletId, string cryptoCode)
    {
        var transactions = new List<NBXTransactionData>();

        try
        {
            var connectionString = GetNBXplorerConnectionString();

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            var query = @"
                SELECT 
                    r.tx_id as ""TransactionId"",
                    r.seen_at as ""Timestamp"",
                    r.balance_change / 100000000.0 as ""BalanceChange"",
                    t.blk_height IS NOT NULL as ""IsConfirmed"",
                    t.blk_height as ""BlockHeight"",
                    CASE WHEN (t.metadata->'fees')::TEXT IS NOT NULL 
                         THEN (t.metadata->'fees')::BIGINT / 100000000.0 
                         ELSE NULL END as ""Fee"",
                    (t.metadata->'feeRate')::NUMERIC as ""FeeRate""
                FROM get_wallets_recent(
                    @walletId,
                    @cryptoCode, 
                    INTERVAL '1000 years',
                    NULL,
                    NULL
                ) r 
                JOIN txs t USING (code, tx_id)
                ORDER BY r.seen_at DESC";

            await using var cmd = new NpgsqlCommand(query, connection);
            cmd.Parameters.AddWithValue("walletId", walletId);
            cmd.Parameters.AddWithValue("cryptoCode", cryptoCode);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                transactions.Add(new NBXTransactionData
                {
                    TransactionId = reader.GetString(0),
                    Timestamp = reader.GetDateTime(1),
                    BalanceChange = reader.GetDecimal(2),
                    IsConfirmed = reader.GetBoolean(3),
                    BlockHeight = reader.IsDBNull(4) ? null : reader.GetInt64(4),
                    Fee = reader.IsDBNull(5) ? null : reader.GetDecimal(5),
                    FeeRate = reader.IsDBNull(6) ? null : reader.GetDecimal(6)
                });
            }
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
            var connectionString = GetNBXplorerConnectionString();

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

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

            await using var cmd = new NpgsqlCommand(query, connection);
            cmd.Parameters.AddWithValue("txId", txId);
            cmd.Parameters.AddWithValue("cryptoCode", cryptoCode);
            cmd.Parameters.AddWithValue("feeSatoshis", (long)(fee * 100_000_000));
            cmd.Parameters.AddWithValue("feeRate", feeRate);

            var rowsAffected = await cmd.ExecuteNonQueryAsync();
            
            if (rowsAffected == 0)
            {
                _logger.LogWarning("No transaction found to update: {TxId}", txId);
            }
            else
            {
                _logger.LogInformation("Updated transaction {TxId} with fee={Fee} BTC, feeRate={FeeRate} sat/vB", 
                    txId, fee, feeRate);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating transaction metadata in NBXplorer database");
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
    public decimal? Fee { get; set; }
    public decimal? FeeRate { get; set; }
    
    public bool HasMissingData => !Fee.HasValue || !FeeRate.HasValue;
}
