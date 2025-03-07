using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;

namespace BTCPayServer.RockstarDev.Plugins.VendorPay.Data;

<<<<<<<< HEAD:Plugins/BTCPayServer.RockstarDev.Plugins.VendorPay/Data/PluginMigrationRunner.cs
internal class PluginMigrationRunner(PluginDbContextFactory dbContextFactory) : IHostedService
========
internal class VendorPayPluginMigrationRunner(VendorPayPluginDbContextFactory dbContextFactory) : IHostedService
>>>>>>>> 2df4aab (Change all instance of payroll to Vendor pay):Plugins/BTCPayServer.RockstarDev.Plugins.VendorPay/Data/VendorPayPluginMigrationRunner.cs
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
