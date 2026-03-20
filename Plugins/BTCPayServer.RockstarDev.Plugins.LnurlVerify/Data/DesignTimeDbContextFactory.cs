#nullable enable
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BTCPayServer.RockstarDev.Plugins.LnurlVerify.Data;

// ReSharper disable once UnusedType.Global
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<LnurlVerifyDbContext>
{
    public LnurlVerifyDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<LnurlVerifyDbContext>();
        builder.UseNpgsql("User ID=postgres;Host=127.0.0.1;Port=39372;Database=designtimebtcpay");
        return new LnurlVerifyDbContext(builder.Options);
    }
}
