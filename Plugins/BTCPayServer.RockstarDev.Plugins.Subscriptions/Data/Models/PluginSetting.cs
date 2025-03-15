using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.RockstarDev.Plugins.Subscriptions.Data.Models;

public class PluginSetting
{
    [MaxLength(50)] public string Key { get; set; }

    [StringLength(50)] [Required] public string StoreId { get; set; }

    public string Value { get; set; }

    public static void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PluginSetting>()
            .HasKey(c => new { c.StoreId, c.Key });
    }
}

public enum PluginSettingKeys
{
    General
}