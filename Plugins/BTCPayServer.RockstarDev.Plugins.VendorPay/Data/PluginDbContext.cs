using System.Threading.Tasks;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Data.Models;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Logic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace BTCPayServer.RockstarDev.Plugins.VendorPay.Data;

public class PluginDbContext(DbContextOptions<PluginDbContext> options, bool designTime = false)
    : DbContext(options)
{

    public DbSet<VendorPayInvoice> PayrollInvoices { get; set; }
    public DbSet<VendorPayUser> PayrollUsers { get; set; }
    public DbSet<VendorPaySetting> PayrollSettings { get; set; }
    public DbSet<VendorPayInvitation> PayrollInvitations { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("BTCPayServer.RockstarDev.Plugins.Payroll");

        VendorPayInvoice.OnModelCreating(modelBuilder);
        VendorPayUser.OnModelCreating(modelBuilder);
    }

    public async Task<VendorPayStoreSetting> GetSettingAsync(string storeId)
    {
        var setting = await PayrollSettings.FirstOrDefaultAsync(a => a.StoreId == storeId);
        if (setting is null)
        {
            return new VendorPayStoreSetting();
        }

        // need to deserialize the setting from json
        var vendorpayStoreSetting = JsonConvert.DeserializeObject<VendorPayStoreSetting>(setting.Setting);
        return vendorpayStoreSetting;
    }
}
