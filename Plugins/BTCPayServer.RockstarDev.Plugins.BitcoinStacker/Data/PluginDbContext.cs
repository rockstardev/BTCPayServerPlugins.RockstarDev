using System;
using BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Data.Models;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Strike.Client.Deposits;

namespace BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Data;

public class PluginDbContext(DbContextOptions<PluginDbContext> options, bool designTime = false)
    : DbContext(options)
{
    public const string DefaultPluginSchema = "BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils";
    
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

    public void AddExchangeOrderLogs(Guid exchangeOrderId, DbExchangeOrderLog.Events evt, object content, string parameter = null)
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
}
