using System;
using System.Collections.Generic;
using BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Data.Models;
using Newtonsoft.Json;

namespace BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.ViewModels.ExchangeOrder;

public class SettingsViewModel
{
    // exchange related settings
    public decimal PercentageOfPayouts { get; set; }
    public int NumberOfBuysToGroupForDeposit { get; set; }
    public DateTimeOffset? StartDateExchangeOrders { get; set; }
    
    //
    public string StrikeApiKey { get; set; }
    public string StripeApiKey { get; set; }
    
    // heartbeat settings
    public int MinutesHeartbeatInterval { get; set; }
    
    public static SettingsViewModel FromDbSettings(DbSetting dbSetting)
    {
        if (dbSetting != null)
        {
            return JsonConvert.DeserializeObject<SettingsViewModel>(dbSetting.Value);
        }
        else
        {
            return new SettingsViewModel
            {
                MinutesHeartbeatInterval = 60,
                NumberOfBuysToGroupForDeposit = 3,
                PercentageOfPayouts = 10,
                StartDateExchangeOrders = DateTimeOffset.UtcNow
            };
        }
    }
}