using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Data.Models;

public class DbSetting
{
    [Key]
    [MaxLength(50)]
    public string Key { get; set; }

    [StringLength(50)]
    [Required]
    public string StoreId { get; set; }

    public string Value { get; set; }

    public static string StrikeApiKey => DbSettingKeys.StrikeApiKey.ToString();

    public static void OnModelCreating(ModelBuilder modelBuilder)
    {
    }
}

public enum DbSettingKeys
{
    StrikeApiKey,
    ExchangeOrderSettings
}
