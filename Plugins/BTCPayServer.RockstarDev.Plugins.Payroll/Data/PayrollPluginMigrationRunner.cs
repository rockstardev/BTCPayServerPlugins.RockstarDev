using BTCPayServer.Abstractions.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Data;

internal class PayrollPluginMigrationRunner : IHostedService
{
    private readonly PayrollPluginDbContextFactory _dbContextFactory;
    private readonly ISettingsRepository _settingsRepository;

    public PayrollPluginMigrationRunner(
        PayrollPluginDbContextFactory dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var ctx = _dbContextFactory.CreateContext();
        await using var dbContext = _dbContextFactory.CreateContext();
        await ctx.Database.MigrateAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
