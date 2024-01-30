using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Data;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<PayrollPluginDbContext>
{
    public PayrollPluginDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<PayrollPluginDbContext> builder = new DbContextOptionsBuilder<PayrollPluginDbContext>();

        // FIXME: Somehow the DateTimeOffset column types get messed up when not using Postgres
        // https://docs.microsoft.com/en-us/ef/core/managing-schemas/migrations/providers?tabs=dotnet-core-cli
        builder.UseNpgsql("User ID=postgres;Host=127.0.0.1;Port=39372;Database=designtimebtcpay");

        return new PayrollPluginDbContext(builder.Options, true);
    }
}
