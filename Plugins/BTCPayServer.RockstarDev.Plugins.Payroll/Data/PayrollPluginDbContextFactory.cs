using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Data
{    public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<PayrollPluginDbContext>
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

    public class LNbankPluginDbContextFactory : BaseDbContextFactory<PayrollPluginDbContext>
    {
        public LNbankPluginDbContextFactory(IOptions<DatabaseOptions> options) : base(options,
            "BTCPayServer.RockstarDev.Plugins.Payroll")
        {
        }

        public override PayrollPluginDbContext CreateContext()
        {
            DbContextOptionsBuilder<PayrollPluginDbContext> builder = new DbContextOptionsBuilder<PayrollPluginDbContext>();
            ConfigureBuilder(builder);
            return new PayrollPluginDbContext(builder.Options);
        }
    }
}
