using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Data.Models;

public class DbSetting
{
    [MaxLength(50)]
    public string Key { get; set; }
    [StringLength(50)]
    [Required]
    public string StoreId { get; set; }

    public string Value { get; set; }

    public static void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DbSetting>()
            .HasKey(c => new { c.StoreId, c.Key });
    }
}

public enum DbSettingKeys
{
    ExchangeOrderSettings
}