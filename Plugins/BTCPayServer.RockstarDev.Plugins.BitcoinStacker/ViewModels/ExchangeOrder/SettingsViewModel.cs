using System;
using BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Data.Models;
using Newtonsoft.Json;

namespace BTCPayServer.RockstarDev.Plugins.BitcoinStacker.ViewModels.ExchangeOrder;

public class SettingsViewModel
{
    // is automatic processing enabled
    public bool AutoEnabled { get; set; }
    
    // exchange related settings
    public decimal PercentageOfPayouts { get; set; }
    public int NumberOfBuysToGroupForDeposit { get; set; }
    public DateTimeOffset? StartDateExchangeOrders { get; set; }
    
    // strike
    public string StrikeApiKey { get; set; }
    
    public Guid StrikePaymentMethodId { get; set; }
    
    // stripe
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