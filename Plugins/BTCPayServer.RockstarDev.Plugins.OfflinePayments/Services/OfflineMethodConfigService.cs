using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.RockstarDev.Plugins.OfflinePayments.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace BTCPayServer.RockstarDev.Plugins.OfflinePayments.Services;
public class OfflineMethodConfigService(OfflinePaymentPluginDbContextFactory pluginDbContextFactory, IMemoryCache cache)
{
    private static string CacheKey(string storeId) => $"offline_methods_{storeId}";

    public void InvalidateCache(string storeId) => cache.Remove(CacheKey(storeId));

    public List<string> GetMethodTypes()
    {
        return ["ACH", "WIRE", "CHECK"];
    }

    public async Task<bool> PaymentMethodExists(string storeId, string methodId)
    {
        await using var ctx = pluginDbContextFactory.CreateContext();
        var normalized = methodId?.Trim().ToUpperInvariant();
        return await ctx.OfflineMethodConfigs.AnyAsync(x => x.StoreId == storeId && x.MethodId == normalized);
    }

    public async Task<List<OfflineMethodConfig>> GetAllMethods(string storeId)
    {
        await using var ctx = pluginDbContextFactory.CreateContext();
        return await ctx.OfflineMethodConfigs.Where(x => x.StoreId == storeId).ToListAsync();
    }

    public async Task<List<OfflineMethodConfig>> GetEnabledMethodOptions(string storeId)
    {

        if (cache.TryGetValue(CacheKey(storeId), out List<OfflineMethodConfig> cached))
            return cached;

        await using var ctx = pluginDbContextFactory.CreateContext();
        var result = await ctx.OfflineMethodConfigs.Where(x => x.StoreId == storeId && x.IsEnabled).ToListAsync();

        cache.Set(CacheKey(storeId), result, TimeSpan.FromSeconds(30));
        return result;
    }

    public async Task<OfflineMethodConfig> GetMethodOptionById(string id, string storeId)
    {
        await using var ctx = pluginDbContextFactory.CreateContext();
        return await ctx.OfflineMethodConfigs.FirstOrDefaultAsync(x => x.Id == id && x.StoreId == storeId);
    }

    public async Task<OfflineMethodConfig> Create(OfflineMethodConfig config, string userId)
    {
        config.CreatedAt = DateTimeOffset.UtcNow;
        config.UpdatedAt = DateTimeOffset.UtcNow;
        config.UserId = userId;
        await using var ctx = pluginDbContextFactory.CreateContext();
        ctx.OfflineMethodConfigs.Add(config);
        await ctx.SaveChangesAsync();
        InvalidateCache(config.StoreId);
        return config;
    }

    public async Task<OfflineMethodConfig> Update(OfflineMethodConfig config)
    {
        await using var ctx = pluginDbContextFactory.CreateContext();
        var existingOption = await ctx.OfflineMethodConfigs.FirstOrDefaultAsync(x => x.Id == config.Id && x.StoreId == config.StoreId);
        if (existingOption is null)
            return null;

        existingOption.DisplayName = config.DisplayName;
        existingOption.Instructions = config.Instructions;
        existingOption.BankName = config.BankName;
        existingOption.BankAddress = config.BankAddress;
        existingOption.AccountName = config.AccountName;
        existingOption.AccountAddress = config.AccountAddress;
        existingOption.RoutingNumber = config.RoutingNumber;
        existingOption.AccountNumber = config.AccountNumber;
        existingOption.ReferenceTemplate = config.ReferenceTemplate;
        existingOption.EstimatedSettlementTime = config.EstimatedSettlementTime;
        existingOption.SupportContact = config.SupportContact;
        existingOption.IsEnabled = config.IsEnabled;
        existingOption.UpdatedAt = DateTimeOffset.UtcNow;
        await ctx.SaveChangesAsync();
        InvalidateCache(config.StoreId);
        return existingOption;
    }

    public async Task<DeleteMethodResult> Delete(string id, string storeId)
    {
        await using var ctx = pluginDbContextFactory.CreateContext();
        var existingOption = await ctx.OfflineMethodConfigs.FirstOrDefaultAsync(x => x.Id == id && x.StoreId == storeId);
        if (existingOption is null)
            return DeleteMethodResult.NotFound;

        var hasAnyPayments = await ctx.OfflinePendingPayments.AnyAsync(x => x.MethodConfigId == id);
        if (hasAnyPayments)
            return DeleteMethodResult.HasPayments;

        ctx.OfflineMethodConfigs.Remove(existingOption);
        await ctx.SaveChangesAsync();
        InvalidateCache(storeId);
        return DeleteMethodResult.Success;
    }
}

public enum DeleteMethodResult
{
    Success,
    NotFound,
    HasPayments
}
