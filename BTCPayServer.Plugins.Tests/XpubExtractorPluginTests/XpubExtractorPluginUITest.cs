using BTCPayServer.Tests;
using Xunit;

namespace BTCPayServer.Plugins.Tests;

[Collection("Plugin Tests")]
[Trait("Category", "PlaywrightUITest")]
public class XpubExtractorPluginUITest : PlaywrightBaseTest
{
    private readonly SharedPluginTestFixture _fixture;

    public XpubExtractorPluginUITest(SharedPluginTestFixture fixture, ITestOutputHelper helper) : base(helper)
    {
        _fixture = fixture;
        if (_fixture.ServerTester == null)
            _fixture.Initialize(this);
        ServerTester = _fixture.ServerTester;
    }

    public ServerTester ServerTester { get; }

    // Issue #142: BTCPay Server 2.4.0 moved _LayoutWalletSetup from Views/UIStores into the
    // Wallets plugin (btcpayserver#7329), so this page threw "The layout view could not be
    // located" on every visit. The layout is resolved at render time, not at build time,
    // so only a request to the page catches it.
    [Fact]
    public async Task CanVisitXpubExtractorPageAsync()
    {
        await InitializePlaywright(ServerTester);
        var account = ServerTester.NewAccount();
        await account.GrantAccessAsync();

        await GoToUrl("/login");
        await LogIn(account.RegisterDetails.Email, account.RegisterDetails.Password);

        var response = await Page.GotoAsync(Link($"/plugins/{account.StoreId}/xpubextractor"));
        Assert.NotNull(response);
        Assert.Equal(200, response.Status);
        Assert.Equal("Xpub Extractor", (await Page.Locator("header h1").InnerTextAsync()).Trim());
        // #CancelWizard is rendered by _LayoutWalletSetup itself, so this shows the page
        // got the wallet setup layout and not some other one.
        Assert.Equal(1, await Page.Locator("#CancelWizard").CountAsync());
    }
}
