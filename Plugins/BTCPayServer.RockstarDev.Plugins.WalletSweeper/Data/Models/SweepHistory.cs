using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BTCPayServer.RockstarDev.Plugins.WalletSweeper.Data.Models;

public class SweepHistory
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public string Id { get; set; } = null!;

    [Required]
    public string SweepConfigurationId { get; set; } = null!;

    public SweepConfiguration SweepConfiguration { get; set; } = null!;

    public DateTimeOffset SweepDate { get; set; }

    [MaxLength(64)]
    public string? TransactionId { get; set; }

    [Column(TypeName = "decimal(18,8)")]
    public decimal Amount { get; set; }

    [Column(TypeName = "decimal(18,8)")]
    public decimal Fee { get; set; }

    [MaxLength(200)]
    public string? DestinationAddress { get; set; }

    [Required]
    [MaxLength(50)]
    public string Status { get; set; } = null!;

    [MaxLength(1000)]
    public string? ErrorMessage { get; set; }

    // Cost basis tracking for swept UTXOs
    [Column(TypeName = "decimal(18,2)")]
    public decimal? WeightedAverageCostBasis { get; set; }

    public int UtxoCount { get; set; }

    [MaxLength(50)]
    public string? TriggerType { get; set; } // "Manual", "Automatic", "Scheduled"
}
