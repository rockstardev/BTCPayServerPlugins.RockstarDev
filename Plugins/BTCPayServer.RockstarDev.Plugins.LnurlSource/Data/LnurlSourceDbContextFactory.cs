#nullable enable
using System;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace BTCPayServer.RockstarDev.Plugins.LnurlSource.Data;

public class LnurlSourceDbContextFactory(IOptions<DatabaseOptions> options)
    : BaseDbContextFactory<LnurlSourceDbContext>(options,
        "BTCPayServer.RockstarDev.Plugins.LnurlSource")
{
    public override LnurlSourceDbContext CreateContext(
        Action<NpgsqlDbContextOptionsBuilder>? npgsqlOptionsAction = null)
    {
        var builder = new DbContextOptionsBuilder<LnurlSourceDbContext>();
        ConfigureBuilder(builder, npgsqlOptionsAction);
        return new LnurlSourceDbContext(builder.Options);
    }
}
