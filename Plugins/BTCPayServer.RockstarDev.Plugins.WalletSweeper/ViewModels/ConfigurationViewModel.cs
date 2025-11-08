using System.ComponentModel.DataAnnotations;
using BTCPayServer.RockstarDev.Plugins.WalletSweeper.Data.Models;

namespace BTCPayServer.RockstarDev.Plugins.WalletSweeper.ViewModels;

public class ConfigurationViewModel
{
    public string? Id { get; set; }
    
    [Required]
    [MaxLength(100)]
    [Display(Name = "Configuration Name")]
    public string ConfigName { get; set; } = string.Empty;
    
    [MaxLength(500)]
    [Display(Name = "Description")]
    public string? Description { get; set; }
    
    [Display(Name = "Seed Phrase (12 or 24 words)")]
    public string SeedPhrase { get; set; } = string.Empty;
    
    [Display(Name = "Encryption Password")]
    [DataType(DataType.Password)]
    public string SeedPassword { get; set; } = string.Empty;
    
    [Display(Name = "Confirm Password")]
    [DataType(DataType.Password)]
    public string SeedPasswordConfirm { get; set; }
    
    [MaxLength(100)]
    [Display(Name = "Derivation Path")]
    public string DerivationPath { get; set; } = "m/84'/1'/0'"; // Default to Native SegWit testnet
    
    [Range(1, 100)]
    [Display(Name = "Address Gap Limit")]
    public int AddressGapLimit { get; set; } = 100;
    
    [Display(Name = "Enabled")]
    public bool Enabled { get; set; } = true;
    
    [Range(0, 21000000)]
    [Display(Name = "Minimum Balance (BTC)")]
    public decimal MinimumBalance { get; set; } = 0.0025m;
    
    [Range(0, 21000000)]
    [Display(Name = "Maximum Balance (BTC)")]
    public decimal MaximumBalance { get; set; } = 0.1m;
    
    [Range(0, 21000000)]
    [Display(Name = "Reserve Amount (BTC)")]
    public decimal ReserveAmount { get; set; } = 0m;
    
    [Range(60, 86400)]
    [Display(Name = "Monitoring Interval (seconds)")]
    public int IntervalSeconds { get; set; } = 600;
    
    [Range(1, 100)]
    [Display(Name = "Fee Rate (sat/vB)")]
    public int FeeRate { get; set; } = 1;
    
    [Display(Name = "Destination Type")]
    public DestinationType DestinationType { get; set; } = DestinationType.ThisStore;
    
    [MaxLength(200)]
    [Display(Name = "Destination Address")]
    public string? DestinationAddress { get; set; }
    
    [Display(Name = "Auto Generate Label")]
    public bool AutoGenerateLabel { get; set; } = true;
    
    // For editing existing configs
    public bool HasEncryptedSeed { get; set; }
    
    public static ConfigurationViewModel FromModel(SweepConfiguration config)
    {
        return new ConfigurationViewModel
        {
            Id = config.Id,
            ConfigName = config.ConfigName,
            Description = config.Description,
            DerivationPath = config.DerivationPath,
            AddressGapLimit = config.AddressGapLimit,
            Enabled = config.AutoEnabled,
            MinimumBalance = config.MinimumBalance,
            MaximumBalance = config.MaximumBalance,
            ReserveAmount = config.ReserveAmount,
            IntervalSeconds = config.IntervalMinutes,
            FeeRate = config.FeeRate,
            DestinationType = config.DestinationType,
            DestinationAddress = config.DestinationAddress,
            AutoGenerateLabel = config.AutoGenerateLabel,
            HasEncryptedSeed = !string.IsNullOrEmpty(config.EncryptedSeed),
            SeedPhrase = string.Empty, // Never populate from model for security
            SeedPassword = string.Empty
        };
    }
}
