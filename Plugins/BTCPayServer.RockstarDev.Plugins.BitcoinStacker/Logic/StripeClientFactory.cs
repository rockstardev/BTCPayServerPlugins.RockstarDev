using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Stripe;

namespace BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Logic;

public class StripeClientFactory
{
    public async Task<List<Payout>> PayoutsAllAsync(string apiKey)
    {
        var payouts = new PayoutService(new StripeClient(apiKey));
        var allPayouts = await payouts.ListAsync();
        return allPayouts.Data;
    }

    public async Task<List<Payout>> PayoutsSince(string apiKey, DateTimeOffset sinceDate)
    {
        var payouts = new PayoutService(new StripeClient(apiKey));
        var payoutsResp = await payouts.ListAsync(new PayoutListOptions
        {
            Created = new DateRangeOptions
            {
                GreaterThan = sinceDate.UtcDateTime
            }
        });
        return payoutsResp.Data;
    }
}