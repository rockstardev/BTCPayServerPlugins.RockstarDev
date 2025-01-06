using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Data;
using BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Strike.Client;

namespace BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Logic;

public class StrikeClientFactory(
    IServiceProvider serviceProvider,
    ILoggerFactory loggerFactory,
    IScopeProvider scopeProvider)
{
    public async Task<bool> IsApiKeyValid(string apiKey)
    {
        var client = InitClient(apiKey);
        var storeId = scopeProvider.GetCurrentStoreId();
        try
        {
            // Test the client with a simple request
            var balances = await client.Balances.GetBalances();
            if (!balances.IsSuccessStatusCode)
            {
                var logger = loggerFactory.CreateLogger<StrikeClientFactory>();
                logger.LogInformation($"The connection failed, check API key. Error: {balances.Error?.Data?.Code} {balances.Error?.Data?.Message}");
                return false;
            }
            
            return true;
        }
        catch
        {
            return false;
        }
    }

    public StrikeClient InitClient(string apiKey)
    {
        var client = serviceProvider.GetRequiredService<StrikeClient>();
        client.ApiKey = apiKey;
        //client.Environment = environment;
        client.ThrowOnError = false;

        //if (serverUrl != null)
        //    client.ServerUrl = serverUrl;
        return client;
    }
}