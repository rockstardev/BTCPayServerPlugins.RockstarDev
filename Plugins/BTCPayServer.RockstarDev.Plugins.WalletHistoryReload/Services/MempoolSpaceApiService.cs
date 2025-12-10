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
    private readonly Random _random = new Random();

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
            // For regtest, generate random fees (1-10 sat/vB)
            if (network.Equals("regtest", StringComparison.OrdinalIgnoreCase))
            {
                return GenerateRandomRegtestFees(txid, blockHash);
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

    private TransactionData GenerateRandomRegtestFees(string txid, string? blockHash)
    {
        // Generate random fee rate between 1-10 sat/vB
        var feeRate = _random.Next(1, 11);
        
        // Assume average transaction size of 250 vBytes
        var estimatedVSize = 250;
        var feeSats = feeRate * estimatedVSize;
        var feeBtc = feeSats / 100_000_000m;

        _logger.LogInformation("Generated random regtest fee for {TxId}: {Fee} BTC ({FeeRate} sat/vB)", 
            txid, feeBtc, feeRate);

        return new TransactionData
        {
            TxId = txid,
            Fee = feeBtc,
            FeeRate = feeRate,
            BlockTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Confirmed = !string.IsNullOrEmpty(blockHash)
        };
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
