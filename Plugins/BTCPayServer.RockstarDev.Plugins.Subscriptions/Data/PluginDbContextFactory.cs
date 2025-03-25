﻿using System;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace BTCPayServer.RockstarDev.Plugins.Subscriptions.Data;

public class PluginDbContextFactory(IOptions<DatabaseOptions> options)
    : BaseDbContextFactory<PluginDbContext>(options, PluginDbContext.Schema)
{
    public override PluginDbContext CreateContext(
        Action<NpgsqlDbContextOptionsBuilder> npgsqlOptionsAction = null)
    {
        var builder = new DbContextOptionsBuilder<PluginDbContext>();
        ConfigureBuilder(builder, npgsqlOptionsAction);
        return new PluginDbContext(builder.Options);
    }

    // public async Task<PayrollStoreSetting> GetSettingAsync(string storeId)
    // {
    //     await using var db = CreateContext();
    //     return await db.GetSettingAsync(storeId);
    // }
    //
    // public async Task<PayrollStoreSetting> SetSettingAsync(string storeId, PayrollStoreSetting setting)
    // {
    //     ArgumentNullException.ThrowIfNull(setting);
    //
    //     await using var db = CreateContext();
    //     var existingSetting = await db.PayrollSettings.FirstOrDefaultAsync(a => a.StoreId == storeId);
    //
    //     if (existingSetting != null)
    //     {
    //         existingSetting.Setting = JsonConvert.SerializeObject(setting);
    //     }
    //     else
    //     {
    //         // Add a new setting since it does not exist
    //         var newSetting = new PayrollSetting
    //         {
    //             StoreId = storeId,
    //             Setting = JsonConvert.SerializeObject(setting)
    //         };
    //         db.PayrollSettings.Add(newSetting);
    //     }
    //
    //     await db.SaveChangesAsync();
    //     return setting;
    // } 
}