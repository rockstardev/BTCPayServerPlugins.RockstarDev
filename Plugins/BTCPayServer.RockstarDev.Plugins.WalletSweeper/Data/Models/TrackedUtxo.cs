using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BTCPayServer.RockstarDev.Plugins.WalletSweeper.Data.Models;

public class TrackedUtxo
{
    // Primary Key
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public string Id { get; set; } = null!;
    
    // Foreign Key
    [Required]
    public string SweepConfigurationId { get; set; } = null!;
    
    public SweepConfiguration SweepConfiguration { get; set; } = null!;
    
    // UTXO identification
    [Required]
    [MaxLength(100)]
    public string Outpoint { get; set; } = null!; // Format: "txid:vout"
    
    [Required]
    [MaxLength(64)]
    public string TxId { get; set; } = null!;
    
    public int Vout { get; set; }
    
    // UTXO details
    [Column(TypeName = "decimal(18,8)")]
    public decimal Amount { get; set; }
    
    [Required]
    [MaxLength(200)]
    public string Address { get; set; } = null!;
    
    [MaxLength(100)]
    public string? DerivationPath { get; set; }
    
    public int Confirmations { get; set; }
    
    // Cost basis tracking
    [Column(TypeName = "decimal(18,2)")]
    public decimal? CostBasisUsd { get; set; }
    
    public DateTimeOffset ReceivedDate { get; set; }
    
    [MaxLength(50)]
    public string? CostBasisSource { get; set; }
    
    // Status
    public bool IsSpent { get; set; } = false;
    
    public DateTimeOffset? SpentDate { get; set; }
    
    [MaxLength(64)]
    public string? SpentInSweepTxId { get; set; }
    
    // Metadata
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
