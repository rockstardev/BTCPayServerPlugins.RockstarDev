using System;
using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Data.Models;

public class DbExchangeOrderLog
{
    public enum Events
    {
        CreatingDeposit,
        DepositCreated,
        ExecutingExchange,
        ExchangeExecuted,
        Error
    }

    public Guid Id { get; set; }
    public Guid ExchangeOrderId { get; set; }
    public DbExchangeOrder ExchangeOrder { get; set; }
    public Events Event { get; set; }
    public string Content { get; set; }
    public DateTimeOffset Created { get; set; }

    [StringLength(50)] public string Parameter { get; set; }
}