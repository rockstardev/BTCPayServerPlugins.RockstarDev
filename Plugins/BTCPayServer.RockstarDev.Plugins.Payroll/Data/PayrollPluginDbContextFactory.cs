using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Data;

public class PayrollPluginDbContextFactory : BaseDbContextFactory<PayrollPluginDbContext>
{
    public PayrollPluginDbContextFactory(IOptions<DatabaseOptions> options) : base(options,
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
