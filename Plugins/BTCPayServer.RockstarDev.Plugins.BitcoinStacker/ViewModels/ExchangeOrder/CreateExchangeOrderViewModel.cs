using System;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Data.Models;

namespace BTCPayServer.RockstarDev.Plugins.BitcoinStacker.ViewModels.ExchangeOrder;

public class CreateExchangeOrderViewModel
{
    [Required]
    public DbExchangeOrder.Operations Operation { get; set; } // Enum for operations

    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than zero.")]
    public decimal Amount { get; set; } // Required amount with range validation

    public DateTimeOffset? DelayUntil { get; set; } // Nullable delay field
}