using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.RockstarDev.Plugins.WalletSweeper.Data.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.RockstarDev.Plugins.WalletSweeper.ViewModels;

public class ConfigurationViewModel
{
    public Guid? Id { get; set; }
    
    public bool Enabled { get; set; }

    // Destination
    [Required]
    [Display(Name = "Destination Address")]
    public string DestinationAddress { get; set; }

    // Thresholds
    [Required]
    [Range(0, 21000000)]
    [Display(Name = "Minimum Balance (BTC)")]
    public decimal MinimumBalance { get; set; } = 0.0005m;

    [Required]
    [Range(0, 21000000)]
    [Display(Name = "Maximum Balance (BTC)")]
    public decimal MaximumBalance { get; set; } = 0.005m;

    [Required]
    [Range(0, 21000000)]
    [Display(Name = "Reserve Amount (BTC)")]
    public decimal ReserveAmount { get; set; } = 0m;

    // Schedule
    [Required]
    [Range(1, 365)]
    [Display(Name = "Sweep Interval (Days)")]
    public int IntervalDays { get; set; } = 7;

    // Settings
    [Required]
    [Display(Name = "Fee Rate")]
    public string FeeRate { get; set; } = "Normal";

    // Cold wallet support
    [Display(Name = "Seed Phrase (for cold wallets)")]
    public string SeedPhrase { get; set; }

    [Display(Name = "Encryption Passphrase")]
    public string SeedPassphrase { get; set; }

    public bool HasEncryptedSeed { get; set; }
    public bool ShowSeedPhrase { get; set; }

    // UI helpers
    public List<SelectListItem> FeeRateOptions { get; set; } = new()
    {
        new SelectListItem { Text = "Economy", Value = "Economy" },
        new SelectListItem { Text = "Normal", Value = "Normal" },
        new SelectListItem { Text = "Priority", Value = "Priority" }
    };

    public bool IsHotWallet { get; set; }
    public string WalletType { get; set; }
    public decimal CurrentBalance { get; set; }

    public static ConfigurationViewModel FromModel(SweepConfiguration config)
    {
        return new ConfigurationViewModel
        {
            Id = config.Id,
            Enabled = config.Enabled,
            DestinationAddress = config.DestinationValue,
            MinimumBalance = config.MinimumBalance,
            MaximumBalance = config.MaximumBalance,
            ReserveAmount = config.ReserveAmount,
            IntervalDays = config.IntervalDays,
            FeeRate = config.FeeRate.ToString(),
            HasEncryptedSeed = !string.IsNullOrEmpty(config.EncryptedSeed),
            SeedPassphrase = config.SeedPassphrase
        };
    }

    public SweepConfiguration ToModel(string storeId)
    {
        return new SweepConfiguration
        {
            Id = Id ?? Guid.NewGuid(),
            StoreId = storeId,
            Enabled = Enabled,
            DestinationValue = DestinationAddress,
            MinimumBalance = MinimumBalance,
            MaximumBalance = MaximumBalance,
            ReserveAmount = ReserveAmount,
            IntervalDays = IntervalDays,
            FeeRate = Enum.Parse<SweepConfiguration.FeeRates>(FeeRate),
            Created = Id.HasValue ? default : DateTimeOffset.UtcNow,
            Updated = DateTimeOffset.UtcNow
        };
    }
}
