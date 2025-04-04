using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Data.Models;

public class DbExchangeOrder
{
    public enum CreateByTypes
    {
        Manual,
        Automatic
    }

    public enum Operations
    {
        BuyBitcoin,
        SellBitcoin,
        Deposit
    }

    public enum States
    {
        Null,
        Created,
        DepositWaiting,
        Completed,
        Error
    }

    public Guid Id { get; set; }

    [StringLength(50)]
    [Required]
    public string StoreId { get; set; }

    public Operations Operation { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset Created { get; set; }
    public DateTimeOffset? DelayUntil { get; set; }

    // differentiate manual and automatic stacking order creations
    [StringLength(50)]
    public string CreatedBy { get; set; }

    // have reference for which date order was created for
    public DateTimeOffset? CreatedForDate { get; set; }
    public States State { get; set; }

    public decimal? TargetAmount { get; set; }
    public decimal? ConversionRate { get; set; }

    [StringLength(50)]
    public string DepositId { get; set; }

    public List<DbExchangeOrderLog> ExchangeOrderLogs { get; set; }

}

