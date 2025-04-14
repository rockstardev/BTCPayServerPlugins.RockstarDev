using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.RockstarDev.Plugins.Stripe.Data;
using BTCPayServer.RockstarDev.Plugins.Stripe.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Stripe;

namespace BTCPayServer.RockstarDev.Plugins.Stripe.Logic;

public class StripeClientFactory(
    StripeDbContextFactory dbContextFactory,
    IServiceProvider serviceProvider,
    ILoggerFactory loggerFactory)
{
    public async Task<bool> TestAndSaveApiKeyAsync(string apiKey)
    {
        StripeConfiguration.ApiKey = apiKey;
        try
        {
            // Test the client with a simple request
            try
            {
                var res = await PayoutsAllAsync();
            }
            catch (Exception ex)
            {
                StripeConfiguration.ApiKey = null;
                var logger = loggerFactory.CreateLogger<StripeClientFactory>();
                logger.LogInformation($"The connection failed, check API key. Error: {ex}");
                return false;
            }

            await using var db = dbContextFactory.CreateContext();
            var setting = await db.Settings.SingleOrDefaultAsync(a => a.Key == DbSetting.StripeApiKey);

            if (setting is null)
                db.Settings.Add(new DbSetting { Key = DbSetting.StripeApiKey, Value = apiKey });
            else
                setting.Value = apiKey;

            await db.SaveChangesAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> ClientExistsAsync()
    {
        await using var db = dbContextFactory.CreateContext();
        var apiKey = db.Settings.SingleOrDefault(a => a.Key == DbSetting.StripeApiKey)?.Value;

        return apiKey != null;
    }

    public async Task<PayoutsAllResp> PayoutsAllAsync(int limit = 100, string startingAfter = null)
    {
        var payouts = new PayoutService();
        var options = new PayoutListOptions
        {
            Limit = limit, StartingAfter = startingAfter // Fetch from this ID onward
        };

        var allPayouts = await payouts.ListAsync(options);
        var resp = new PayoutsAllResp { HasNext = allPayouts.HasMore, Payouts = allPayouts.Data };
        return resp;
    }

    public class PayoutsAllResp
    {
        public List<Payout> Payouts { get; set; }
        public bool HasNext { get; set; }
    }
}
