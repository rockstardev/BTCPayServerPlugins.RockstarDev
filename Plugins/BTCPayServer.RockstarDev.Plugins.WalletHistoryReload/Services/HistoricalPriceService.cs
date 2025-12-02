using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.RockstarDev.Plugins.WalletHistoryReload.Services;

public class HistoricalPriceService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HistoricalPriceService> _logger;
    private readonly Dictionary<string, decimal> _priceCache = new();

    public HistoricalPriceService(IHttpClientFactory httpClientFactory, ILogger<HistoricalPriceService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<decimal?> GetHistoricalBtcPriceAsync(DateTimeOffset timestamp)
    {
        var dateKey = timestamp.ToString("yyyy-MM-dd");
        
        // Check cache first
        if (_priceCache.TryGetValue(dateKey, out var cachedPrice))
            return cachedPrice;

        try
        {
            // Use CoinGecko API for historical prices
            var date = timestamp.ToString("dd-MM-yyyy");
            var client = _httpClientFactory.CreateClient();
            var response = await client.GetAsync(
                $"https://api.coingecko.com/api/v3/coins/bitcoin/history?date={date}");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch historical BTC price for {Date}: {StatusCode}", 
                    date, response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<CoinGeckoHistoricalData>(json);

            var price = data?.MarketData?.CurrentPrice?.Usd;
            
            if (price.HasValue)
            {
                _priceCache[dateKey] = price.Value;
            }

            return price;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching historical BTC price for {Date}", dateKey);
            return null;
        }
    }

    private class CoinGeckoHistoricalData
    {
        public MarketDataContainer? MarketData { get; set; }
    }

    private class MarketDataContainer
    {
        public CurrentPriceContainer? CurrentPrice { get; set; }
    }

    private class CurrentPriceContainer
    {
        public decimal Usd { get; set; }
    }
}
