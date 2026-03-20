#nullable enable
using System;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace BTCPayServer.RockstarDev.Plugins.LnurlVerify.Data;

public class LnurlVerifyDbContextFactory(IOptions<DatabaseOptions> options)
    : BaseDbContextFactory<LnurlVerifyDbContext>(options,
        "BTCPayServer.RockstarDev.Plugins.LnurlVerify")
{
    public override LnurlVerifyDbContext CreateContext(
        Action<NpgsqlDbContextOptionsBuilder>? npgsqlOptionsAction = null)
    {
        var builder = new DbContextOptionsBuilder<LnurlVerifyDbContext>();
        ConfigureBuilder(builder, npgsqlOptionsAction);
        return new LnurlVerifyDbContext(builder.Options);
    }
}
