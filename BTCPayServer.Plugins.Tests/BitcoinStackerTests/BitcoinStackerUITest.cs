using BTCPayServer.Tests;
using Xunit;
using Xunit.Abstractions;
using static BTCPayServer.Plugins.Tests.CashPluginUITest;

namespace BTCPayServer.Plugins.Tests;

public class BitcoinStackerUITest : PlaywrightBaseTest, IClassFixture<BitcoinStackerUITest.BitcoinStackerServerTesterFixture>
{
    private readonly BitcoinStackerServerTesterFixture _fixture;

    public BitcoinStackerUITest(BitcoinStackerServerTesterFixture fixture, ITestOutputHelper helper) : base(helper)
    {
        _fixture = fixture;
        if (_fixture.ServerTester == null) _fixture.Initialize(this);
        ServerTester = _fixture.ServerTester;
    }

    public ServerTester ServerTester { get; }
    public string TestDir { get; private set; }

    [Fact]
    public async Task CanAccessBitcoinStackerPages()
    {
        await InitializePlaywright(ServerTester.PayTester.ServerUri);
        await InitializeBTCPayServer();

        await GoToUrl($"/plugins/{StoreId}/exchangeorder/index");
        
        await Page.Locator("#CreateExchangeOrder").ClickAsync();

        var createButton = await Page.QuerySelectorAsync("input#Create");
        Assert.NotNull(createButton);

        await createButton.ClickAsync();
        
        // ensure validation errors are shown
        Assert.True(await Page.Locator(".text-danger:has-text('Amount must be greater than zero.')").IsVisibleAsync());
    }

    public class BitcoinStackerServerTesterFixture : IDisposable
    {
        public ServerTester ServerTester { get; private set; }

        public void Dispose()
        {
            ServerTester?.Dispose();
            ServerTester = null;
        }

        public void Initialize(PlaywrightBaseTest testInstance)
        {
            if (ServerTester == null)
            {
                var testDir = Path.Combine(Directory.GetCurrentDirectory(), "BitcoinStackerUITest");
                ServerTester = testInstance.CreateServerTester(testDir, true);
                ServerTester.StartAsync().GetAwaiter().GetResult();
            }
        }
    }
}
