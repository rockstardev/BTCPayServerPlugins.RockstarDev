using System;
using System.Collections.Generic;
using System.Threading;
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

    public async Task<List<Payout>> PayoutsSince(string apiKey, DateTimeOffset sinceDate, CancellationToken cancellationToken)
    {
        var payouts = new PayoutService(new StripeClient(apiKey));
        var options = new PayoutListOptions { Limit = 100, Created = new DateRangeOptions { GreaterThan = sinceDate.UtcDateTime } };
        // Read every page: a single page holds only the newest payouts, and the heartbeat's cursor
        // would then move past the older ones and never import them.
        var result = new List<Payout>();
        await foreach (var payout in payouts.ListAutoPagingAsync(options, cancellationToken: cancellationToken))
            result.Add(payout);
        return result;
    }
}
