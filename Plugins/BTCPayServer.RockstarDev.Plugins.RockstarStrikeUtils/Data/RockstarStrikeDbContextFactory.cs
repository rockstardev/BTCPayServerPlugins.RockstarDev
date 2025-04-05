using System;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Data;

public class RockstarStrikeDbContextFactory(IOptions<DatabaseOptions> options)
    : BaseDbContextFactory<RockstarStrikeDbContext>(options, RockstarStrikeDbContext.DefaultPluginSchema)
{
    public override RockstarStrikeDbContext CreateContext(
        Action<NpgsqlDbContextOptionsBuilder> npgsqlOptionsAction = null)
    {
        var builder = new DbContextOptionsBuilder<RockstarStrikeDbContext>();
        ConfigureBuilder(builder, npgsqlOptionsAction);
        return new RockstarStrikeDbContext(builder.Options);
    }
}
