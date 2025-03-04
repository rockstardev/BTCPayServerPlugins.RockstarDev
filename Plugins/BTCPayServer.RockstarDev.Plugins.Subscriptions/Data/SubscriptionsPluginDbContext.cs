using System.Threading.Tasks;
using BTCPayServer.RockstarDev.Plugins.Subscriptions.Data.Models;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace BTCPayServer.RockstarDev.Plugins.Subscriptions.Data;

public class SubscriptionsPluginDbContext(DbContextOptions<SubscriptionsPluginDbContext> options, bool designTime = false)
    : DbContext(options)
{
    public DbSet<Subscription> Subscriptions { get; set; }
    public DbSet<SubscriptionReminder> SubscriptionReminders { get; set; }
    public DbSet<Customer> Customers { get; set; }
    public DbSet<Product> Products { get; set; }
    public DbSet<PluginSetting> PluginSettings { get; set; }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("BTCPayServer.RockstarDev.Plugins.Subscriptions");

        Subscription.OnModelCreating(modelBuilder);
        Customer.OnModelCreating(modelBuilder);
        Product.OnModelCreating(modelBuilder);
    }

    // public async Task<PluginSetting?> GetSettingAsync(string storeId, PluginSettingKeys key)
    // {
    //     var setting = await PluginSettings.FirstOrDefaultAsync(a => 
    //         a.StoreId == storeId && a.Key == key.ToString());
    //     if (setting is null)
    //     {
    //         return new PluginSetting();
    //     }
    //
    //     // need to deserialize the setting from json
    //     var payrollStoreSetting = JsonConvert.DeserializeObject<PluginSetting>(setting.Value);
    //     return payrollStoreSetting;
    // }
}
