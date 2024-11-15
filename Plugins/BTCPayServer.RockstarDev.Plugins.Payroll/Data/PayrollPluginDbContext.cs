using System.Threading.Tasks;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data.Models;
using BTCPayServer.RockstarDev.Plugins.Payroll.Logic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Data;

public class PayrollPluginDbContext : DbContext
{
    // ReSharper disable once NotAccessedField.Local
    private readonly bool _designTime;

    public PayrollPluginDbContext(DbContextOptions<PayrollPluginDbContext> options, bool designTime = false)
        : base(options)
    {
        _designTime = designTime;
    }

    public DbSet<PayrollInvoice> PayrollInvoices { get; set; }
    public DbSet<PayrollUser> PayrollUsers { get; set; }
    public DbSet<PayrollSetting> PayrollSettings { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("BTCPayServer.RockstarDev.Plugins.Payroll");

        PayrollInvoice.OnModelCreating(modelBuilder);
        PayrollUser.OnModelCreating(modelBuilder);
    }

    public async Task<PayrollStoreSetting> GetSettingAsync(string storeId)
    {
        var setting = await PayrollSettings.FirstOrDefaultAsync(a => a.StoreId == storeId);
        if (setting is null)
        {
            return new PayrollStoreSetting();
        }

        // need to deserialize the setting from json
        var payrollStoreSetting = JsonConvert.DeserializeObject<PayrollStoreSetting>(setting.Setting);
        return payrollStoreSetting;
    }
}
