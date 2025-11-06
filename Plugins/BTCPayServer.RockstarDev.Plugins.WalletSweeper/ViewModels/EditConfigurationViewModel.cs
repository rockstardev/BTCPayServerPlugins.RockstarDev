using System.ComponentModel.DataAnnotations;
using BTCPayServer.RockstarDev.Plugins.WalletSweeper.Data.Models;

namespace BTCPayServer.RockstarDev.Plugins.WalletSweeper.ViewModels;

public class EditConfigurationViewModel
{
    [Required]
    public string Id { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(100)]
    [Display(Name = "Configuration Name")]
    public string ConfigName { get; set; } = string.Empty;
    
    [MaxLength(500)]
    [Display(Name = "Description")]
    public string? Description { get; set; }
    
    // Read-only display fields
    public string DerivationPath { get; set; } = string.Empty;
    
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
    
    public static EditConfigurationViewModel FromModel(SweepConfiguration config)
    {
        return new EditConfigurationViewModel
        {
            Id = config.Id,
            ConfigName = config.ConfigName,
            Description = config.Description,
            DerivationPath = config.DerivationPath ?? string.Empty,
            AddressGapLimit = config.AddressGapLimit,
            Enabled = config.Enabled,
            MinimumBalance = config.MinimumBalance,
            MaximumBalance = config.MaximumBalance,
            ReserveAmount = config.ReserveAmount,
            IntervalSeconds = config.IntervalSeconds,
            FeeRate = config.FeeRate,
            DestinationType = config.DestinationType,
            DestinationAddress = config.DestinationAddress,
            AutoGenerateLabel = config.AutoGenerateLabel
        };
    }
}
