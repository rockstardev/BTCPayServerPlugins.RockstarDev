using System;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace BTCPayServer.RockstarDev.Plugins.WalletSweeper.Data;

public class PluginDbContextFactory(IOptions<DatabaseOptions> options)
    : BaseDbContextFactory<PluginDbContext>(options, "BTCPayServer.RockstarDev.Plugins.WalletSweeper")
{
    public override PluginDbContext CreateContext(
        Action<NpgsqlDbContextOptionsBuilder> npgsqlOptionsAction = null)
    {
        var builder = new DbContextOptionsBuilder<PluginDbContext>();
        ConfigureBuilder(builder, npgsqlOptionsAction);
        //builder.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
        return new PluginDbContext(builder.Options);
    }
}
