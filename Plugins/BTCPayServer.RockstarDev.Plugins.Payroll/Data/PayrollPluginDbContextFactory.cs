using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using System;
using System.Threading.Tasks;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data.Models;
using BTCPayServer.RockstarDev.Plugins.Payroll.Logic;
using Newtonsoft.Json;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Data;

public class PayrollPluginDbContextFactory(IOptions<DatabaseOptions> options)
    : BaseDbContextFactory<PayrollPluginDbContext>(options,
        "BTCPayServer.RockstarDev.Plugins.Payroll")
{
    public override PayrollPluginDbContext CreateContext(
        Action<NpgsqlDbContextOptionsBuilder> npgsqlOptionsAction = null)
    {
        var builder = new DbContextOptionsBuilder<PayrollPluginDbContext>();
        ConfigureBuilder(builder, npgsqlOptionsAction);
        return new PayrollPluginDbContext(builder.Options);
    }

    public async Task<PayrollStoreSetting> GetSettingAsync(string storeId)
    {
        await using var db = CreateContext();
        return await db.GetSettingAsync(storeId);
    }

    public async Task<PayrollStoreSetting> SetSettingAsync(string storeId, PayrollStoreSetting setting)
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
            var newSetting = new PayrollSetting
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