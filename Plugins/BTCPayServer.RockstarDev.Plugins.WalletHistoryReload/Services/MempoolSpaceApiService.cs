using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using BTCPayServer;
using BTCPayServer.Services;
using Dapper;
using Microsoft.Extensions.Logging;
using NBitcoin;

namespace BTCPayServer.RockstarDev.Plugins.WalletHistoryReload.Services;

public class MempoolSpaceApiService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ExplorerClientProvider _explorerClientProvider;
    private readonly NBXplorerConnectionFactory _nbxConnectionFactory;
    private readonly ILogger<MempoolSpaceApiService> _logger;

    public MempoolSpaceApiService(
        IHttpClientFactory httpClientFactory, 
        ExplorerClientProvider explorerClientProvider,
        NBXplorerConnectionFactory nbxConnectionFactory,
        ILogger<MempoolSpaceApiService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _explorerClientProvider = explorerClientProvider;
        _nbxConnectionFactory = nbxConnectionFactory;
        _logger = logger;
    }

    private string GetBaseUrl(string network)
    {
        return network?.ToLowerInvariant() switch
        {
            "testnet" => "https://mempool.space/testnet/api",
            "signet" => "https://mempool.space/signet/api",
            _ => "https://mempool.space/api"
        };
    }

    public async Task<TransactionData?> GetTransactionDataAsync(string txid, string network = "mainnet", string cryptoCode = "BTC", string? blockHash = null)
    {
        try
        {
            // For regtest, use Bitcoin Core RPC instead of Mempool.space
            if (network.Equals("regtest", StringComparison.OrdinalIgnoreCase))
            {
                return await GetTransactionFromBitcoinCoreAsync(txid, cryptoCode, blockHash);
            }

            var baseUrl = GetBaseUrl(network);
            var client = _httpClientFactory.CreateClient();
            var response = await client.GetAsync($"{baseUrl}/tx/{txid}");
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch transaction {TxId} from Mempool.space: {StatusCode}", 
                    txid, response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<MempoolTransaction>(json);

            if (data == null)
                return null;

            // Calculate fee rate: fee / vsize
            var vsize = data.Weight / 4.0m;
            var feeRate = vsize > 0 ? data.Fee / vsize : 0;

            return new TransactionData
            {
                TxId = txid,
                Fee = data.Fee / 100_000_000m, // Convert satoshis to BTC
                FeeRate = feeRate,
                BlockTime = data.Status?.BlockTime ?? 0,
                Confirmed = data.Status?.Confirmed ?? false
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching transaction data for {TxId}", txid);
            return null;
        }
    }

    private async Task<Dictionary<string, string>> GetBlockHashesFromNBXplorerAsync(string cryptoCode, IEnumerable<string> txIds)
    {
        var result = new Dictionary<string, string>();
        
        try
        {
            var connection = await _nbxConnectionFactory.OpenConnection();
            if (connection == null)
                return result;

            await using var _ = connection;

            var query = @"
                SELECT t.tx_id, b.blk_id
                FROM txs t
                JOIN blks b ON (t.code = b.code AND t.blk_height = b.height)
                WHERE t.code = @cryptoCode AND t.tx_id = ANY(@txIds)";

            var rows = await connection.QueryAsync<dynamic>(new CommandDefinition(
                commandText: query,
                parameters: new { cryptoCode, txIds = txIds.ToArray() }));

            foreach (var row in rows)
            {
                result[row.tx_id] = row.blk_id;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching block hashes from NBXplorer");
        }

        return result;
    }

    private async Task<TransactionData?> GetTransactionFromBitcoinCoreAsync(string txid, string cryptoCode, string? blockHash = null)
    {
        try
        {
            var explorerClient = _explorerClientProvider.GetExplorerClient(cryptoCode);
            if (explorerClient?.RPCClient == null)
            {
                _logger.LogWarning("RPC client not available for {CryptoCode}", cryptoCode);
                return null;
            }

            var rpcClient = explorerClient.RPCClient;
            var txHash = uint256.Parse(txid);
            uint256? blockHashUint = !string.IsNullOrEmpty(blockHash) ? uint256.Parse(blockHash) : null;
            
            // Get the transaction
            var tx = await rpcClient.GetRawTransactionAsync(txHash, blockHashUint, throwIfNotFound: false);
            if (tx == null && blockHashUint != null)
            {
                // Try without block hash for mempool transactions
                tx = await rpcClient.GetRawTransactionAsync(txHash, null, throwIfNotFound: false);
            }
            
            if (tx == null)
            {
                _logger.LogWarning("Transaction {TxId} not found in Bitcoin Core", txid);
                return null;
            }

            // Calculate fee by summing inputs and outputs
            decimal totalInput = 0;
            decimal totalOutput = tx.Outputs.Sum(o => o.Value.ToDecimal(MoneyUnit.BTC));

            // Get block hashes for all input transactions from NBXplorer
            var inputTxIds = tx.Inputs.Where(i => !i.PrevOut.IsNull).Select(i => i.PrevOut.Hash.ToString()).ToList();
            var inputBlockHashes = await GetBlockHashesFromNBXplorerAsync(cryptoCode, inputTxIds);

            // Get input values
            foreach (var input in tx.Inputs.Where(i => !i.PrevOut.IsNull))
            {
                try
                {
                    var inputTxId = input.PrevOut.Hash.ToString();
                    uint256? inputBlockHash = inputBlockHashes.TryGetValue(inputTxId, out var bhStr) ? uint256.Parse(bhStr) : null;
                    
                    var prevTx = await rpcClient.GetRawTransactionAsync(input.PrevOut.Hash, inputBlockHash, throwIfNotFound: false);
                    if (prevTx != null && input.PrevOut.N < prevTx.Outputs.Count)
                    {
                        totalInput += prevTx.Outputs[(int)input.PrevOut.N].Value.ToDecimal(MoneyUnit.BTC);
                    }
                    else
                    {
                        _logger.LogWarning("Could not fetch input transaction {InputTxId} for {TxId}", input.PrevOut.Hash, txid);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error fetching input transaction {InputTxId} for {TxId}", input.PrevOut.Hash, txid);
                }
            }

            decimal fee = totalInput - totalOutput;
            if (fee < 0) fee = 0; // Coinbase or error

            // Calculate fee rate (sat/vB)
            var vsize = tx.GetVirtualSize();
            decimal feeRate = vsize > 0 ? (fee * 100_000_000m) / vsize : 0;

            // Get block time if confirmed
            long blockTime = 0;
            bool confirmed = false;
            
            if (blockHashUint != null)
            {
                try
                {
                    var block = await rpcClient.GetBlockAsync(blockHashUint);
                    if (block != null)
                    {
                        blockTime = block.Header.BlockTime.ToUnixTimeSeconds();
                        confirmed = true;
                    }
                    else
                    {
                        _logger.LogWarning("Block {BlockHash} not found for transaction {TxId}", blockHashUint, txid);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error fetching block {BlockHash} for transaction {TxId}", blockHashUint, txid);
                }
            }

            return new TransactionData
            {
                TxId = txid,
                Fee = fee,
                FeeRate = feeRate,
                BlockTime = blockTime,
                Confirmed = confirmed
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching transaction from Bitcoin Core for {TxId}", txid);
            return null;
        }
    }

    private class MempoolTransaction
    {
        public long Fee { get; set; }
        public int Weight { get; set; }
        public TransactionStatus? Status { get; set; }
    }

    private class TransactionStatus
    {
        public bool Confirmed { get; set; }
        public long BlockTime { get; set; }
    }
}

public class TransactionData
{
    public string TxId { get; set; } = string.Empty;
    public decimal Fee { get; set; }
    public decimal FeeRate { get; set; }
    public long BlockTime { get; set; }
    public bool Confirmed { get; set; }
}
