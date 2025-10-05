using BTCPayServer.Abstractions.Models;
using BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Plugins.Tests.BitcoinStackerTests;

/// <summary>
/// Manual data seeder for BitcoinStacker plugin.
/// All tests in this class are skipped by default to prevent running in CI.
/// To run manually: dotnet test --filter "FullyQualifiedName~BitcoinStackerDataSeeder"
/// </summary>
[Trait("Category", "Manual")]
public class BitcoinStackerDataSeeder
{
    private readonly ITestOutputHelper _output;
    private const string DefaultConnectionString = "User ID=postgres;Include Error Detail=true;Host=127.0.0.1;Port=39372;Database=btcpayserver";

    public BitcoinStackerDataSeeder(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Creates a PluginDbContextFactory from the BTCPAY_POSTGRES environment variable
    /// Falls back to default connection string if not set
    /// </summary>
    private PluginDbContextFactory CreateDbContextFactory()
    {
        var connectionString = Environment.GetEnvironmentVariable("BTCPAY_POSTGRES") ?? DefaultConnectionString;
        _output.WriteLine($"Using connection string: {connectionString}");

        // Create IOptions<DatabaseOptions> wrapper
        var databaseOptions = new DatabaseOptions { ConnectionString = connectionString };
        var options = Options.Create(databaseOptions);
        
        return new PluginDbContextFactory(options);
    }

    /// <summary>
    /// Seed 250 test exchange orders to the development database
    /// </summary>
    [Fact]
    [Trait("Category", "ManualDataSeeder")]
    public async Task Seed_250_Orders()
    {
        var factory = CreateDbContextFactory();
        
        var storeId = "5QjVv1zSs8JEwBn3YGrF3ZeBHrQDhJLzAFVdKQsZSx1u"; // TODO: Replace with actual store ID
        _output.WriteLine($"Generating 250 test orders for store: {storeId}");
        
        await BitcoinStackerPluginUITest.GenerateTestExchangeOrders(factory, storeId, 250);
        
        // Verify count
        await using var db = factory.CreateContext();
        var count = db.ExchangeOrders.Count(o => o.StoreId == storeId);
        
        _output.WriteLine($"✓ Successfully seeded {count} exchange orders");
    }

    /// <summary>
    /// Clear all exchange orders for a specific store
    /// </summary>
    [Fact]
    [Trait("Category", "ManualDataSeeder")]
    public async Task Clear_All_Orders()
    {
        var factory = CreateDbContextFactory();
        
        var storeId = "5QjVv1zSs8JEwBn3YGrF3ZeBHrQDhJLzAFVdKQsZSx1u"; // TODO: Replace with actual store ID
        _output.WriteLine($"Clearing all orders for store: {storeId}");
        
        await using var db = factory.CreateContext();
        var orders = db.ExchangeOrders.Where(o => o.StoreId == storeId);
        var count = await orders.CountAsync();
        
        db.ExchangeOrders.RemoveRange(orders);
        await db.SaveChangesAsync();
        
        _output.WriteLine($"✓ Deleted {count} exchange orders");
    }
}
