using System;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace BTCPayServer.RockstarDev.Plugins.Stripe.Data;

public class StripeDbContextFactory(IOptions<DatabaseOptions> options)
    : BaseDbContextFactory<StripeDbContext>(options, StripeDbContext.DefaultPluginSchema)
{
    public override StripeDbContext CreateContext(
        Action<NpgsqlDbContextOptionsBuilder> npgsqlOptionsAction = null)
    {
        var builder = new DbContextOptionsBuilder<StripeDbContext>();
        ConfigureBuilder(builder, npgsqlOptionsAction);
        return new StripeDbContext(builder.Options);
    }
}
