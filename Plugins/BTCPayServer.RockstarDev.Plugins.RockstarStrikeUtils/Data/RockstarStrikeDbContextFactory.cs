using System;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Data.Models;
using BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.ViewModels.ExchangeOrder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
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