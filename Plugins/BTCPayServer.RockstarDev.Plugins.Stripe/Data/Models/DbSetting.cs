using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.RockstarDev.Plugins.Stripe.Data.Models;

public class DbSetting
{
    [Key]
    [MaxLength(50)]
    public string Key { get; set; }

    public string Value { get; set; }

    public static void OnModelCreating(ModelBuilder modelBuilder)
    {
    }
}

public enum DbSettingKeys
{
    StripeApiKey
}