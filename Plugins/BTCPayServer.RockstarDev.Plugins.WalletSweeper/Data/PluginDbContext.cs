using BTCPayServer.RockstarDev.Plugins.WalletSweeper.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.RockstarDev.Plugins.WalletSweeper.Data;

public class PluginDbContext : DbContext
{
    public const string DefaultPluginSchema = "BTCPayServer.RockstarDev.Plugins.WalletSweeper";

    public PluginDbContext(DbContextOptions<PluginDbContext> options, bool designTime = false) : base(options)
    {
    }

    public DbSet<SweepConfiguration> SweepConfigurations { get; set; }
    public DbSet<SweepHistory> SweepHistories { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema(DefaultPluginSchema);

        // Configure SweepConfiguration
        modelBuilder.Entity<SweepConfiguration>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.StoreId);
            entity.Property(e => e.MinimumBalance).HasPrecision(18, 8);
            entity.Property(e => e.MaximumBalance).HasPrecision(18, 8);
            entity.Property(e => e.ReserveAmount).HasPrecision(18, 8);
        });

        // Configure SweepHistory
        modelBuilder.Entity<SweepHistory>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ConfigurationId);
            entity.HasIndex(e => e.StoreId);
            entity.HasIndex(e => e.Timestamp);
            entity.Property(e => e.Amount).HasPrecision(18, 8);
            entity.Property(e => e.Fee).HasPrecision(18, 8);

            // Configure relationship
            entity.HasOne(e => e.Configuration)
                .WithMany()
                .HasForeignKey(e => e.ConfigurationId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
