using System;
using System.Collections.Generic;
using BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Data.Models;

namespace BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.ViewModels.ExchangeOrder;

public class SettingsViewModel
{
    // exchange related settings
    public decimal PercentageOfPayouts { get; set; }
    public int NumberOfBuysToGroupForDeposit { get; set; }
    public DateTimeOffset? StartDateExchangeOrders { get; set; }
    
    // heartbeat settings
    public int MinutesHeartbeatInterval { get; set; }
}