using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Data;
using BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Logic;

public class StrikeClientFactory(
    PluginDbContextFactory strikeDbContextFactory,
    IServiceProvider serviceProvider,
    ILoggerFactory loggerFactory,
    IScopeProvider scopeProvider)
{
    private string StoreId => scopeProvider.GetCurrentStoreId();
    
    public async Task<bool> TestAndSaveApiKeyAsync(string apiKey)
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

            await using var db = strikeDbContextFactory.CreateContext();
            var setting = await db.Settings.SingleOrDefaultAsync(a => a.StoreId == StoreId && a.Key == DbSetting.StrikeApiKey);

            if (setting is null)
                db.Settings.Add(new DbSetting { Key = DbSetting.StrikeApiKey, StoreId = StoreId, Value = apiKey });
            else
                setting.Value = apiKey;

            await db.SaveChangesAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> ClientExistsAsync()
    {
        await using var db = strikeDbContextFactory.CreateContext();
        var apiKey = db.Settings.SingleOrDefault(a => a.StoreId == StoreId && a.Key == DbSetting.StrikeApiKey)?.Value;

        return apiKey != null;
    }


    public async Task<StrikeClient> ClientCreateAsync()
    {
        await using var db = strikeDbContextFactory.CreateContext();
        var apiKey = db.Settings.SingleOrDefault(a =>  a.StoreId == StoreId && a.Key == DbSetting.StrikeApiKey)?.Value;
        if (apiKey is null)
        {
            throw new InvalidOperationException("API key not found in the database.");
        }

        return InitClient(apiKey);
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