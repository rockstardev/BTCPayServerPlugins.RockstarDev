using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.RockstarDev.Plugins.WalletHistoryReload.Services;

public class MempoolSpaceApiService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MempoolSpaceApiService> _logger;
    private const string BaseUrl = "https://mempool.space/api";

    public MempoolSpaceApiService(IHttpClientFactory httpClientFactory, ILogger<MempoolSpaceApiService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<TransactionData?> GetTransactionDataAsync(string txid)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            var response = await client.GetAsync($"{BaseUrl}/tx/{txid}");
            
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
