using BTCPayServer.Abstractions.Models;
using BTCPayServer.Tests;
using Xunit;
using Xunit.Abstractions;
using static BTCPayServer.Plugins.Tests.CashPluginUITest;

namespace BTCPayServer.Plugins.Tests;

public class TransactionCounterUITest : PlaywrightBaseTest, IClassFixture<TransactionCounterUITest.TransactionCounterServerTesterFixture>
{
    private readonly TransactionCounterServerTesterFixture _fixture;

    public TransactionCounterUITest(TransactionCounterServerTesterFixture fixture, ITestOutputHelper helper) : base(helper)
    {
        _fixture = fixture;
        if (_fixture.ServerTester == null) _fixture.Initialize(this);
        ServerTester = _fixture.ServerTester;
    }

    public ServerTester ServerTester { get; }
    public string TestDir { get; private set; }

    [Fact]
    public async Task CanAccessTransactionCounterPages()
    {
        await InitializePlaywright(ServerTester.PayTester.ServerUri);
        await InitializeBTCPayServer();

        await GoToUrl($"/server/stores/counter");

        var saveButton = await Page.QuerySelectorAsync("button#page-primary");
        Assert.NotNull(saveButton);
        await saveButton.ClickAsync();
        
        // ensure validation errors are shown
        var statusText = await (await FindAlertMessageAsync(StatusMessageModel.StatusSeverity.Success)).TextContentAsync();
        var textPresent = statusText?.Trim() == "Plugin counter configuration updated successfully";
        Assert.True(textPresent);
    }

    public class TransactionCounterServerTesterFixture : IDisposable
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
                var testDir = Path.Combine(Directory.GetCurrentDirectory(), "TransactionCounterUITest");
                ServerTester = testInstance.CreateServerTester(testDir, true);
                ServerTester.StartAsync().GetAwaiter().GetResult();
            }
        }
    }
}
