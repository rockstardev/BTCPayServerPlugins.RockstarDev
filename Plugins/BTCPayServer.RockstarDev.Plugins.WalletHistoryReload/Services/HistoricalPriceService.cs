using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
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
        var cacheKey = timestamp.ToString("yyyy-MM-dd-HH");

        // Check cache first
        if (_priceCache.TryGetValue(cacheKey, out var cachedPrice))
            return cachedPrice;

        try
        {
            var age = DateTimeOffset.UtcNow - timestamp;

            // For transactions < 90 days old, use hourly data from market_chart/range
            if (age.TotalDays < 90)
            {
                var price = await GetHourlyPriceAsync(timestamp);
                if (price.HasValue) _priceCache[cacheKey] = price.Value;
                return price;
            }
            // For older transactions, use daily data from history endpoint
            else
            {
                var price = await GetDailyPriceAsync(timestamp);
                if (price.HasValue) _priceCache[cacheKey] = price.Value;
                return price;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching historical BTC price for {Timestamp}", timestamp);
            return null;
        }
    }

    private async Task<decimal?> GetHourlyPriceAsync(DateTimeOffset timestamp)
    {
        try
        {
            // Use market_chart/range endpoint for hourly granularity
            // Query a 2-hour window around the timestamp to get the closest data point
            var fromTimestamp = timestamp.AddHours(-1).ToUnixTimeSeconds();
            var toTimestamp = timestamp.AddHours(1).ToUnixTimeSeconds();

            var client = _httpClientFactory.CreateClient();
            var response = await client.GetAsync(
                $"https://api.coingecko.com/api/v3/coins/bitcoin/market_chart/range?vs_currency=usd&from={fromTimestamp}&to={toTimestamp}");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch hourly BTC price for {Timestamp}: {StatusCode}",
                    timestamp, response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<CoinGeckoMarketChartData>(json);

            // Find the price closest to our target timestamp
            if (data?.Prices != null && data.Prices.Count > 0)
            {
                var targetUnix = timestamp.ToUnixTimeSeconds() * 1000; // CoinGecko uses milliseconds
                var closestPrice = data.Prices
                    .OrderBy(p => Math.Abs(p[0] - targetUnix))
                    .FirstOrDefault();

                if (closestPrice != null && closestPrice.Count >= 2) return closestPrice[1];
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching hourly price for {Timestamp}", timestamp);
            return null;
        }
    }

    private async Task<decimal?> GetDailyPriceAsync(DateTimeOffset timestamp)
    {
        try
        {
            // Use history endpoint for daily data
            var date = timestamp.ToString("dd-MM-yyyy");
            var client = _httpClientFactory.CreateClient();
            var response = await client.GetAsync(
                $"https://api.coingecko.com/api/v3/coins/bitcoin/history?date={date}");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch daily BTC price for {Date}: {StatusCode}",
                    date, response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<CoinGeckoHistoricalData>(json);

            return data?.MarketData?.CurrentPrice?.Usd;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching daily price for {Date}", timestamp.ToString("yyyy-MM-dd"));
            return null;
        }
    }

    private class CoinGeckoMarketChartData
    {
        [JsonPropertyName("prices")]
        public List<List<decimal>>? Prices { get; set; }
    }

    private class CoinGeckoHistoricalData
    {
        [JsonPropertyName("market_data")]
        public MarketDataContainer? MarketData { get; set; }
    }

    private class MarketDataContainer
    {
        [JsonPropertyName("current_price")]
        public CurrentPriceContainer? CurrentPrice { get; set; }
    }

    private class CurrentPriceContainer
    {
        [JsonPropertyName("usd")]
        public decimal Usd { get; set; }
    }
}
