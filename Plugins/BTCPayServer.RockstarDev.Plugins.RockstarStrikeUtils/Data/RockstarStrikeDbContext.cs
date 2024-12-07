using BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Data;

public class RockstarStrikeDbContext(DbContextOptions<RockstarStrikeDbContext> options, bool designTime = false)
    : DbContext(options)
{
    public const string DefaultPluginSchema = "BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils";
    
    public DbSet<DbSetting> Settings { get; set; }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(DefaultPluginSchema);

        DbSetting.OnModelCreating(modelBuilder);
    }
}
