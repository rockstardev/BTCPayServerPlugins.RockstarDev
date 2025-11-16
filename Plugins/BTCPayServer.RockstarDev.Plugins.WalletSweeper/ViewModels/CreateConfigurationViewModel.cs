using System.ComponentModel.DataAnnotations;
using BTCPayServer.RockstarDev.Plugins.WalletSweeper.Data.Models;

namespace BTCPayServer.RockstarDev.Plugins.WalletSweeper.ViewModels;

public class CreateConfigurationViewModel
{
    [Required]
    [MaxLength(100)]
    [Display(Name = "Configuration Name")]
    public string ConfigName { get; set; } = string.Empty;

    [MaxLength(500)]
    [Display(Name = "Description")]
    public string? Description { get; set; }

    [Required]
    [Display(Name = "Seed Phrase (12 or 24 words)")]
    public string SeedPhrase { get; set; } = string.Empty;

    [Required]
    [MinLength(4)]
    [Display(Name = "Encryption Password")]
    [DataType(DataType.Password)]
    public string SeedPassword { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Confirm Password")]
    [DataType(DataType.Password)]
    [Compare(nameof(SeedPassword), ErrorMessage = "Passwords do not match")]
    public string SeedPasswordConfirm { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    [Display(Name = "Derivation Path")]
    public string DerivationPath { get; set; } = "m/84'/1'/0'";

    [Range(1, 100)]
    [Display(Name = "Address Gap Limit")]
    public int AddressGapLimit { get; set; } = 100;

    [Display(Name = "AutoEnabled")]
    public bool AutoEnabled { get; set; } = true;

    [Range(0, 21000000)]
    [Display(Name = "Minimum Balance (BTC)")]
    public decimal MinimumBalance { get; set; } = 0.0025m;

    [Range(0, 21000000)]
    [Display(Name = "Maximum Balance (BTC)")]
    public decimal MaximumBalance { get; set; } = 0.1m;

    [Range(0, 21000000)]
    [Display(Name = "Reserve Amount (BTC)")]
    public decimal ReserveAmount { get; set; } = 0m;

    [Range(1, 10000)]
    [Display(Name = "Monitoring Interval (minutes)")]
    public int IntervalMinutes { get; set; } = 10;

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
}
