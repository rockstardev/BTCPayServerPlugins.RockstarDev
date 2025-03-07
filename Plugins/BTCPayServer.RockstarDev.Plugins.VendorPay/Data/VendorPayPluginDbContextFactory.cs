using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Data.Models;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Logic;

namespace BTCPayServer.RockstarDev.Plugins.VendorPay.Data;

public class VendorPayPluginDbContextFactory(IOptions<DatabaseOptions> options)
    : BaseDbContextFactory<VendorPayPluginDbContext>(options,
        "BTCPayServer.RockstarDev.Plugins.Payroll")
{
    public override VendorPayPluginDbContext CreateContext(
        Action<NpgsqlDbContextOptionsBuilder> npgsqlOptionsAction = null)
    {
        var builder = new DbContextOptionsBuilder<VendorPayPluginDbContext>();
        ConfigureBuilder(builder, npgsqlOptionsAction);
        return new VendorPayPluginDbContext(builder.Options);
    }

    public async Task<VendorPayStoreSetting> GetSettingAsync(string storeId)
    {
        await using var db = CreateContext();
        return await db.GetSettingAsync(storeId);
    }

    public async Task<VendorPayStoreSetting> SetSettingAsync(string storeId, VendorPayStoreSetting setting)
    {
        ArgumentNullException.ThrowIfNull(setting);

        await using var db = CreateContext();
        var existingSetting = await db.PayrollSettings.FirstOrDefaultAsync(a => a.StoreId == storeId);

        if (existingSetting != null)
        {
            existingSetting.Setting = JsonConvert.SerializeObject(setting);
        }
        else
        {
            // Add a new setting since it does not exist
            var newSetting = new VendorPaySetting
            {
                StoreId = storeId,
                Setting = JsonConvert.SerializeObject(setting)
            };
            db.PayrollSettings.Add(newSetting);
        }

        await db.SaveChangesAsync();
        return setting;
    } 
}