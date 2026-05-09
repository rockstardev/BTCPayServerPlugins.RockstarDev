using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.RockstarDev.Plugins.OfflinePayments.Data;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.RockstarDev.Plugins.OfflinePayments.Services;
public class OfflineMethodConfigService(OfflinePaymentPluginDbContextFactory pluginDbContextFactory)
{
    public async Task<bool> ExistsAsync(string storeId, string methodId)
    {
        await using var ctx = pluginDbContextFactory.CreateContext();
        return await ctx.OfflineMethodConfigs.AnyAsync(x => x.StoreId == storeId && x.MethodId == methodId.ToUpperInvariant());
    }

    public async Task<List<OfflineMethodConfig>> GetAllMethods(string storeId)
    {
        await using var ctx = pluginDbContextFactory.CreateContext();
        return await ctx.OfflineMethodConfigs.Where(x => x.StoreId == storeId)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.DisplayName).ToListAsync();
    }

    public async Task<List<OfflineMethodConfig>> GetEnabledMethodOptions(string storeId)
    {
        await using var ctx = pluginDbContextFactory.CreateContext();
        return await ctx.OfflineMethodConfigs.Where(x => x.StoreId == storeId && x.IsEnabled)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.DisplayName).ToListAsync();
    }

    public async Task<OfflineMethodConfig> GetMethodOptionById(string id, string storeId)
    {
        await using var ctx = pluginDbContextFactory.CreateContext();
        return await ctx.OfflineMethodConfigs.FirstOrDefaultAsync(x => x.Id == id && x.StoreId == storeId);
    }

    public async Task<OfflineMethodConfig> Create(OfflineMethodConfig config)
    {
        config.CreatedAt = DateTimeOffset.UtcNow;
        config.UpdatedAt = DateTimeOffset.UtcNow;
        await using var ctx = pluginDbContextFactory.CreateContext();
        ctx.OfflineMethodConfigs.Add(config);
        await ctx.SaveChangesAsync();
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
        existingOption.RequiresManualConfirmation = config.RequiresManualConfirmation;
        existingOption.IsEnabled = config.IsEnabled;
        existingOption.SortOrder = config.SortOrder;
        existingOption.UpdatedAt = DateTimeOffset.UtcNow;
        await ctx.SaveChangesAsync();
        return existingOption;
    }

    public async Task<bool> Delete(string id, string storeId)
    {
        await using var ctx = pluginDbContextFactory.CreateContext();
        var existingOption = await ctx.OfflineMethodConfigs.FirstOrDefaultAsync(x => x.Id == id && x.StoreId == storeId);
        if (existingOption is null)
            return false;

        ctx.OfflineMethodConfigs.Remove(existingOption);
        await ctx.SaveChangesAsync();
        return true;
    }
}
