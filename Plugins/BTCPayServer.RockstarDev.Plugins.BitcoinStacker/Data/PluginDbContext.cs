using System;
using BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Data.Models;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Data;

public class PluginDbContext(DbContextOptions<PluginDbContext> options, bool designTime = false)
    : DbContext(options)
{
    public const string DefaultPluginSchema = "BTCPayServer.RockstarDev.Plugins.BitcoinStacker";

    public DbSet<DbSetting> Settings { get; set; }

    // For supporting exchange orders
    public DbSet<DbExchangeOrder> ExchangeOrders { get; set; }
    public DbSet<DbExchangeOrderLog> ExchangeOrderLogs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(DefaultPluginSchema);

        DbSetting.OnModelCreating(modelBuilder);
    }

    public void AddExchangeOrderLogs(Guid exchangeOrderId, DbExchangeOrderLog.Events evt, object content,
        string parameter = null)
    {
        var log = new DbExchangeOrderLog
        {
            ExchangeOrderId = exchangeOrderId,
            Created = DateTimeOffset.UtcNow,
            Event = evt,
            Content = JsonConvert.SerializeObject(content),
            Parameter = parameter
        };
        ExchangeOrderLogs.Add(log);
    }

    public DbSetting SettingAddOrUpdate(string storeId, DbSettingKeys key, object modelToSave)
    {
        var setting = Settings.Find(storeId, key.ToString());
        if (setting == null)
        {
            setting = new DbSetting
            {
                StoreId = storeId,
                Key = key.ToString(),
                Value = JsonConvert.SerializeObject(modelToSave)
            };
            Settings.Add(setting);
        }
        else
        {
            setting.Value = JsonConvert.SerializeObject(modelToSave);
        }

        return setting;
    }

    public DbSetting SettingFetch(string storeId, DbSettingKeys key)
    {
        return Settings.Find(storeId, key.ToString());
    }
}
