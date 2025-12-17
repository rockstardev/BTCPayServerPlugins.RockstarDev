using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Data;

internal class PluginMigrationRunner(PluginDbContextFactory dbContextFactory) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var ctx = dbContextFactory.CreateContext();
        await ctx.Database.MigrateAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
