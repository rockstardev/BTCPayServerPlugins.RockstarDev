using System;
using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.RockstarDev.Plugins.WalletSweeper.Data.Models;

public class SweepConfiguration
{
    public enum FeeRates
    {
        Economy,
        Normal,
        Priority
    }

    public Guid Id { get; set; }

    [StringLength(50)]
    [Required]
    public string StoreId { get; set; }

    public bool Enabled { get; set; }

    [StringLength(200)]
    [Required]
    public string DestinationValue { get; set; } // StoreId or BTC address

    // Thresholds (in BTC)
    public decimal MinimumBalance { get; set; }
    public decimal MaximumBalance { get; set; }
    public decimal ReserveAmount { get; set; } // Amount to always keep in source wallet

    // Schedule
    public int IntervalDays { get; set; }
    public DateTimeOffset? LastSweepDate { get; set; }

    // Settings
    public FeeRates FeeRate { get; set; }

    // Cold wallet support (encrypted seed phrase)
    [StringLength(1000)]
    public string EncryptedSeed { get; set; } // Encrypted mnemonic seed phrase

    [StringLength(500)]
    public string SeedPassphrase { get; set; } // Passphrase used for encryption (stored as hint)

    public DateTimeOffset Created { get; set; }
    public DateTimeOffset Updated { get; set; }
}
