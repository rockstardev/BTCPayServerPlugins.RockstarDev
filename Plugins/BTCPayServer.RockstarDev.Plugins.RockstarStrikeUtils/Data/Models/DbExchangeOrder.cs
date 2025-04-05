using System;
using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Data.Models;

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
        Completed
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
    public DateTimeOffset? Executed { get; set; }
    public decimal? CostBasis { get; set; }
}

public class DbExchangeOrderLog
{
    public Guid Id { get; set; }
    public Guid ExchangeOrderId { get; set; }
    public string Content { get; set; }
    public DateTimeOffset Created { get; set; }
    public string Parameters { get; set; }
}
