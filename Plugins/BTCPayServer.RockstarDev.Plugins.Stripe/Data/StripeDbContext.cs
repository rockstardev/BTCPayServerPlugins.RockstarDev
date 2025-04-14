using BTCPayServer.RockstarDev.Plugins.Stripe.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.RockstarDev.Plugins.Stripe.Data;

public class StripeDbContext(DbContextOptions<StripeDbContext> options, bool designTime = false)
    : DbContext(options)
{
    public const string DefaultPluginSchema = "BTCPayServer.RockstarDev.Plugins.Stripe";

    public DbSet<DbSetting> Settings { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(DefaultPluginSchema);

        DbSetting.OnModelCreating(modelBuilder);
    }
}
