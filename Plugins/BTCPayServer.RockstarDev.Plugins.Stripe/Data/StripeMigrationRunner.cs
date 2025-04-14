using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.RockstarDev.Plugins.Stripe.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Stripe;

namespace BTCPayServer.RockstarDev.Plugins.Stripe.Data;

internal class StripeMigrationRunner(StripeDbContextFactory dbContextFactory) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var ctx = dbContextFactory.CreateContext();
        await using var dbContext = dbContextFactory.CreateContext();
        await ctx.Database.MigrateAsync(cancellationToken);

        // initialize stripe api key if needed
        var apiKey = dbContext.Settings.SingleOrDefault(a => a.Key == DbSetting.StripeApiKey)?.Value;
        if (!string.IsNullOrEmpty(apiKey)) StripeConfiguration.ApiKey = apiKey;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
