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
    public DbSet<TrackedUtxo> TrackedUtxos { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema(DefaultPluginSchema);

        // Configure SweepConfiguration
        modelBuilder.Entity<SweepConfiguration>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.StoreId);
            entity.HasIndex(e => new { e.StoreId, e.ConfigName });
            entity.Property(e => e.MinimumBalance).HasPrecision(18, 8);
            entity.Property(e => e.MaximumBalance).HasPrecision(18, 8);
            entity.Property(e => e.ReserveAmount).HasPrecision(18, 8);
            entity.Property(e => e.CurrentBalance).HasPrecision(18, 8);
        });

        // Configure TrackedUtxo
        modelBuilder.Entity<TrackedUtxo>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Outpoint).IsUnique();
            entity.HasIndex(e => e.SweepConfigurationId);
            entity.HasIndex(e => e.IsSpent);
            entity.HasIndex(e => new { e.SweepConfigurationId, e.IsSpent });
            entity.Property(e => e.Amount).HasPrecision(18, 8);
            entity.Property(e => e.CostBasisUsd).HasPrecision(18, 2);

            // Configure relationship
            entity.HasOne(e => e.SweepConfiguration)
                .WithMany(e => e.TrackedUtxos)
                .HasForeignKey(e => e.SweepConfigurationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure SweepHistory
        modelBuilder.Entity<SweepHistory>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.SweepConfigurationId);
            entity.HasIndex(e => e.SweepDate);
            entity.Property(e => e.Amount).HasPrecision(18, 8);
            entity.Property(e => e.Fee).HasPrecision(18, 8);
            entity.Property(e => e.WeightedAverageCostBasis).HasPrecision(18, 2);

            // Configure relationship
            entity.HasOne(e => e.SweepConfiguration)
                .WithMany()
                .HasForeignKey(e => e.SweepConfigurationId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
