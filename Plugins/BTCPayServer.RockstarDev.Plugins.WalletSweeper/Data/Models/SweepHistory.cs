using System;
using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.RockstarDev.Plugins.WalletSweeper.Data.Models;

public class SweepHistory
{
    public enum SweepStatuses
    {
        Pending,
        Success,
        Failed
    }

    public enum TriggerTypes
    {
        Scheduled,
        Manual,
        MaxThreshold
    }

    public Guid Id { get; set; }

    public Guid ConfigurationId { get; set; }

    [StringLength(50)]
    [Required]
    public string StoreId { get; set; }

    public DateTimeOffset Timestamp { get; set; }

    public decimal Amount { get; set; } // Amount swept (excluding fee)
    public decimal Fee { get; set; }

    [StringLength(200)]
    public string Destination { get; set; }

    [StringLength(100)]
    public string TxId { get; set; }

    public SweepStatuses Status { get; set; }

    public TriggerTypes TriggerType { get; set; }

    [StringLength(500)]
    public string ErrorMessage { get; set; }

    // Navigation property
    public SweepConfiguration Configuration { get; set; }
}
