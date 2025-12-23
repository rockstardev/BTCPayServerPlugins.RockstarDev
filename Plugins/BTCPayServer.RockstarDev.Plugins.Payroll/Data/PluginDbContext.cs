using System.Threading.Tasks;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Data.Models;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Logic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace BTCPayServer.RockstarDev.Plugins.VendorPay.Data;

public class PluginDbContext(DbContextOptions<PluginDbContext> options, bool designTime = false)
    : DbContext(options)
{
    public DbSet<PayrollInvoice> PayrollInvoices { get; set; }
    public DbSet<PayrollUser> PayrollUsers { get; set; }
    public DbSet<PayrollSetting> PayrollSettings { get; set; }
    public DbSet<PayrollInvitation> PayrollInvitations { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // we keep the original database schema since we don't want to migrate it right now
        modelBuilder.HasDefaultSchema("BTCPayServer.RockstarDev.Plugins.Payroll");

        PayrollInvoice.OnModelCreating(modelBuilder);
        PayrollUser.OnModelCreating(modelBuilder);
    }

    public async Task<VendorPayStoreSetting> GetSettingAsync(string storeId)
    {
        var setting = await PayrollSettings.FirstOrDefaultAsync(a => a.StoreId == storeId);
        if (setting is null) return new VendorPayStoreSetting();

        // need to deserialize the setting from json
        var payrollStoreSetting = JsonConvert.DeserializeObject<VendorPayStoreSetting>(setting.Setting);
        return payrollStoreSetting;
    }
}
