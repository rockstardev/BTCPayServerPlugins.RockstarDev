using System.Threading.Tasks;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Plugins.Tests;
using BTCPayServer.Tests;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Plugins.Tests;

[Collection("Plugin Tests")]
[Trait("Category", "PlaywrightUITest")]
public class WalletHistoryReloadPluginUITest : PlaywrightBaseTest
{
    private readonly SharedPluginTestFixture _fixture;

    public WalletHistoryReloadPluginUITest(SharedPluginTestFixture fixture, ITestOutputHelper helper) : base(helper)
    {
        _fixture = fixture;
        if (_fixture.ServerTester == null)
            _fixture.Initialize(this);
        ServerTester = _fixture.ServerTester;
    }

    public ServerTester ServerTester { get; }

    [Fact]
    public async Task CanVisitWalletHistoryReloadPageAsync()
    {
        await InitializePlaywright(ServerTester);
        var account = ServerTester.NewAccount();
        await account.GrantAccessAsync();
        await account.MakeAdmin(true);

        await GoToUrl("/login");
        await LogIn(account.RegisterDetails.Email, account.RegisterDetails.Password);

        await ServerTester.ExplorerNode.GenerateAsync(1);
        var walletId = await account.RegisterDerivationSchemeAsync("BTC", importKeysToNBX: true);
        WalletId = walletId;
        var storeId = account.StoreId;

        await GoToUrl($"/{storeId}/plugins/wallet-history-reload/BTC");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        
        var pageContent = await Page.ContentAsync();
        Assert.Contains("Wallet History Reload", pageContent);
        Assert.Contains("Wallet Information", pageContent);
        Assert.Contains("Total Transactions", pageContent);
        
        TestLogs.LogInformation("✓ Successfully visited Wallet History Reload plugin page");
    }
}
