#nullable enable
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BTCPayServer.RockstarDev.Plugins.LnurlSource.Data;

// ReSharper disable once UnusedType.Global
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<LnurlSourceDbContext>
{
    public LnurlSourceDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<LnurlSourceDbContext>();
        builder.UseNpgsql("User ID=postgres;Host=127.0.0.1;Port=39372;Database=designtimebtcpay");
        return new LnurlSourceDbContext(builder.Options);
    }
}
