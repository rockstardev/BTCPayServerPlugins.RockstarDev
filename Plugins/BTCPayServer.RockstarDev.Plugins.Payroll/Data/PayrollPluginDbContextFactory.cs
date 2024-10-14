using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using System;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Data;

public class PayrollPluginDbContextFactory : BaseDbContextFactory<PayrollPluginDbContext>
{
    public PayrollPluginDbContextFactory(IOptions<DatabaseOptions> options) : base(options,
        "BTCPayServer.RockstarDev.Plugins.Payroll")
    {
    }
    public override PayrollPluginDbContext CreateContext(Action<NpgsqlDbContextOptionsBuilder> npgsqlOptionsAction = null)
    {
        var builder = new DbContextOptionsBuilder<PayrollPluginDbContext>();
        ConfigureBuilder(builder, npgsqlOptionsAction);
        return new PayrollPluginDbContext(builder.Options);
    }
}
