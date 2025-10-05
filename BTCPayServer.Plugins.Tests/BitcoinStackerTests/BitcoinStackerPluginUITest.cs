using BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Data;
using BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Data.Models;
using BTCPayServer.Tests;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Plugins.Tests.BitcoinStackerTests;

[Collection("Plugin Tests")]
public class BitcoinStackerPluginUITest : PlaywrightBaseTest
{
    private readonly SharedPluginTestFixture _fixture;

    public BitcoinStackerPluginUITest(SharedPluginTestFixture fixture, ITestOutputHelper helper) : base(helper)
    {
        _fixture = fixture;
        if (_fixture.ServerTester == null) _fixture.Initialize(this);
        ServerTester = _fixture.ServerTester;
    }

    public ServerTester ServerTester { get; }

    [Fact]
    public async Task CanAccessExchangeOrdersPage()
    {
        await InitializePlaywright(ServerTester);
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin(true);
        
        await GoToUrl("/login");
        await LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
        
        // Navigate to Exchange Orders page
        await GoToUrl($"/plugins/{user.StoreId}/exchangeorder/index");
        
        // Verify page loaded
        var pageTitle = await Page.TextContentAsync("h2");
        Assert.Contains("Exchange Orders", pageTitle);
    }

    [Fact]
    public async Task PaginationWorks()
    {
        await InitializePlaywright(ServerTester);
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin(true);
        
        // Generate test data first
        await GenerateTestExchangeOrders(user.StoreId, 150);
        
        await GoToUrl("/login");
        await LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
        
        // Navigate to Exchange Orders page
        await GoToUrl($"/plugins/{user.StoreId}/exchangeorder/index");
        
        // Check that pagination controls exist
        var paginationExists = await Page.QuerySelectorAsync("nav[aria-label='...']");
        Assert.NotNull(paginationExists);
        
        // Verify "Next" button exists (since we have 150 items and default is 100 per page)
        var nextButton = await Page.QuerySelectorAsync("a.page-link:has-text('Next')");
        Assert.NotNull(nextButton);
        
        // Click next and verify URL changed
        await nextButton.ClickAsync();
        Assert.Contains("skip=100", Page.Url);
    }

    /// <summary>
    /// Helper method to generate test exchange orders
    /// Can be called from tests with the ServerTester's factory
    /// </summary>
    private async Task GenerateTestExchangeOrders(string storeId, int count = 250)
    {
        // TODO: Try to fetch database context directly, this wasn't working
        // dbContextFactory = ServerTester.PayTester.GetService<PluginDbContextFactory>();
        var databaseOptions = ServerTester.PayTester.GetService<Microsoft.Extensions.Options.IOptions<BTCPayServer.Abstractions.Models.DatabaseOptions>>();
        PluginDbContextFactory dbContextFactory = new PluginDbContextFactory(databaseOptions);
        
        await GenerateTestExchangeOrders(dbContextFactory, storeId, count);
    }

    /// <summary>
    /// Static helper to generate test exchange orders with a provided factory
    /// Can be used in both tests and manual seeding
    /// </summary>
    public static async Task GenerateTestExchangeOrders(PluginDbContextFactory dbContextFactory, string storeId, int count = 250)
    {
        await using var db = dbContextFactory.CreateContext();
        
        var random = new Random();
        var operations = new[] { DbExchangeOrder.Operations.BuyBitcoin, DbExchangeOrder.Operations.SellBitcoin };
        var states = Enum.GetValues<DbExchangeOrder.States>();
        var createdByOptions = new[] { "Manual", "Automated", "Scheduled", "API" };
        
        var orders = new List<DbExchangeOrder>();
        // Dates MUST be UTC because of postgres
        var startDate = DateTimeOffset.UtcNow.AddMonths(-6); 
        for (int i = 0; i < count; i++)
        {
            var daysAgo = random.Next(0, 180);
            var createdDate = startDate.AddDays(daysAgo).AddHours(random.Next(0, 24));
            var operation = operations[random.Next(operations.Length)];
            var state = states.GetValue(random.Next(states.Length)) as DbExchangeOrder.States? ?? DbExchangeOrder.States.Created;
            
            var order = new DbExchangeOrder
            {
                StoreId = storeId,
                Operation = operation,
                Amount = (decimal)(random.Next(10, 1000) + random.NextDouble()), // $10 - $1000
                Created = createdDate.ToUniversalTime(),
                CreatedForDate = new DateTimeOffset(createdDate.Date, TimeSpan.Zero),
                State = state,
                CreatedBy = createdByOptions[random.Next(createdByOptions.Length)]
            };
            
            // Add conversion data for completed orders
            if (state == DbExchangeOrder.States.Completed)
            {
                var btcPrice = random.Next(30000, 70000); // BTC price between $30k-$70k
                order.ConversionRate = btcPrice;
                order.TargetAmount = order.Amount / btcPrice; // Calculate BTC amount
            }
            
            // Always delay orders for at least 5 years for testing purposes
            order.DelayUntil = DateTimeOffset.UtcNow.AddYears(5);
            
            orders.Add(order);
        }
        
        db.ExchangeOrders.AddRange(orders);
        await db.SaveChangesAsync();
    }
}
