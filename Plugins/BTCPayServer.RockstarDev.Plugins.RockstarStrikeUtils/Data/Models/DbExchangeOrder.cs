using System;
using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Data.Models;

public class DbExchangeOrder
{
    public Guid Id { get; set; }
    [StringLength(50)]
    [Required]
    public string StoreId { get; set; }
    public Operations Operation { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset Created { get; set; }
    public DateTimeOffset? DelayUntil { get; set; }
    public string CreatedBy { get; set; }
    public States State { get; set; }
    public DateTimeOffset? Executed { get; set; }
    public decimal? CostBasis { get; set; }
    
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
        Processing,
        DepositWaiting,
        Completed
    }
}

public class DbExchangeOrderLog
{
    public Guid Id { get; set; }
    public Guid ExchangeOrderId { get; set; }
    public string Content { get; set; }
    public DateTimeOffset Created { get; set; }
    public string Parameters { get; set; }
}