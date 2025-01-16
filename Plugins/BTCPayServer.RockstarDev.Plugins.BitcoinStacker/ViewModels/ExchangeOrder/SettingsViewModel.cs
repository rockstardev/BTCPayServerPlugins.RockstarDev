using System;
using BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Data.Models;
using Newtonsoft.Json;

namespace BTCPayServer.RockstarDev.Plugins.BitcoinStacker.ViewModels.ExchangeOrder;

public class SettingsViewModel
{
    // is automatic processing enabled
    public bool AutoEnabled { get; set; }

    public int? DelayOrderDays { get; set; }

    // exchange related settings
    public decimal PercentageOfPayouts { get; set; }
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
            var json = JsonConvert.DeserializeObject<SettingsViewModel>(dbSetting.Value);
            json.DelayOrderDays ??= 365;

            return json;
        }

        return new SettingsViewModel
        {
            MinutesHeartbeatInterval = 60,
            PercentageOfPayouts = 10,
            StartDateExchangeOrders = DateTimeOffset.UtcNow,
            DelayOrderDays = 365
        };
    }
}