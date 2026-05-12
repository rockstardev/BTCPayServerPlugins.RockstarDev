using System;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace BTCPayServer.RockstarDev.Plugins.OfflinePayments.Data;

public class OfflinePaymentPluginDbContext(DbContextOptions<OfflinePaymentPluginDbContext> options, bool designTime = false)
    : DbContext(options)
{
    public DbSet<OfflineMethodConfig> OfflineMethodConfigs { get; set; }
    public DbSet<OfflinePendingPayment> OfflinePendingPayments { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("BTCPayServer.RockstarDev.Plugins.OfflinePayment");

        OfflineMethodConfig.OnModelCreating(modelBuilder);
        OfflinePendingPayment.OnModelCreating(modelBuilder);
    }
}


public class OfflinePaymentPluginDbContextFactory(IOptions<DatabaseOptions> options)
    : BaseDbContextFactory<OfflinePaymentPluginDbContext>(options, "BTCPayServer.RockstarDev.Plugins.OfflinePayment")
{
    public override OfflinePaymentPluginDbContext CreateContext(
        Action<NpgsqlDbContextOptionsBuilder> npgsqlOptionsAction = null)
    {
        var builder = new DbContextOptionsBuilder<OfflinePaymentPluginDbContext>();
        ConfigureBuilder(builder, npgsqlOptionsAction);
        return new OfflinePaymentPluginDbContext(builder.Options);
    }
}
