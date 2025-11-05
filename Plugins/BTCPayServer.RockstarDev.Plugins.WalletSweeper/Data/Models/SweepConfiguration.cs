using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BTCPayServer.RockstarDev.Plugins.WalletSweeper.Data.Models;

public class SweepConfiguration
{
    // Primary Key
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public string Id { get; set; } = null!;
    
    // Store relationship
    [Required]
    [MaxLength(50)]
    public string StoreId { get; set; } = null!;
    
    // Config identification
    [Required]
    [MaxLength(100)]
    public string ConfigName { get; set; } = null!;
    
    [MaxLength(500)]
    public string? Description { get; set; }
    
    // External wallet configuration
    [MaxLength(1000)]
    public string? EncryptedSeed { get; set; }
    
    [MaxLength(200)]
    public string? AccountXpub { get; set; }
    
    [MaxLength(100)]
    public string? DerivationPath { get; set; }
    
    public int AddressGapLimit { get; set; } = 100;
    
    // Sweep settings
    public bool Enabled { get; set; } = true;
    
    [Column(TypeName = "decimal(18,8)")]
    public decimal MinimumBalance { get; set; } = 0.005m;
    
    [Column(TypeName = "decimal(18,8)")]
    public decimal MaximumBalance { get; set; } = 0.1m;
    
    public int IntervalSeconds { get; set; } = 600;
    
    [Column(TypeName = "decimal(18,8)")]
    public decimal ReserveAmount { get; set; } = 0m;
    
    public int FeeRate { get; set; } = 1;
    
    // Destination configuration
    public DestinationType DestinationType { get; set; } = DestinationType.ThisStore;
    
    [MaxLength(200)]
    public string? DestinationAddress { get; set; }
    
    public bool AutoGenerateLabel { get; set; } = true;
    
    // Monitoring state
    public DateTimeOffset? LastMonitored { get; set; }
    
    public DateTimeOffset? LastSwept { get; set; }
    
    [Column(TypeName = "decimal(18,8)")]
    public decimal CurrentBalance { get; set; } = 0m;
    
    // Metadata
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
    
    public DateTimeOffset Updated { get; set; } = DateTimeOffset.UtcNow;
    
    // Navigation properties
    public List<TrackedUtxo> TrackedUtxos { get; set; } = new();
}

public enum DestinationType
{
    ThisStore = 0,
    CustomAddress = 1
}
