using BTCPayServer.Abstractions.Models;
using BTCPayServer.Tests;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Plugins.Tests;

[Collection("Plugin Tests")]
[Trait("Category", "PlaywrightUITest")]
public class WithdrawalProviderPluginUITest : PlaywrightBaseTest
{
    private readonly SharedPluginTestFixture _fixture;

    public WithdrawalProviderPluginUITest(SharedPluginTestFixture fixture, ITestOutputHelper helper) : base(helper)
    {
        _fixture = fixture;
        if (_fixture.ServerTester == null) _fixture.Initialize(this);
        ServerTester = _fixture.ServerTester;
    }

    public ServerTester ServerTester { get; }

    [Fact]
    public async Task CanNavigateToWithdrawalProviderPageFromStoreAsync()
    {
        var user = await CreateAndLoginAdminAsync();

        await GoToUrl($"/stores/{user.StoreId}");
        await Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Withdrawal Provider" }).ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        Assert.Contains($"/plugins/{user.StoreId}/withdrawal-provider", Page.Url);
        var heading = await Page.Locator("h2").TextContentAsync();
        Assert.Contains("Withdrawal Provider", heading);

        var navLink = Page.Locator("a.nav-link", new PageLocatorOptions { HasTextString = "Withdrawal Provider" });
        var navClass = await navLink.GetAttributeAsync("class");
        Assert.Contains("active", navClass);
    }

    [Fact]
    public async Task CanSaveSettingsAndKeepNormalizedValuesAsync()
    {
        var user = await CreateAndLoginAdminAsync();

        await GoToUrl($"/plugins/{user.StoreId}/withdrawal-provider");
        await Page.FillAsync("#ApiKey", "test-api-key");
        await Page.FillAsync("#Ticker", "btcusd");
        await Page.FillAsync("#FiatCurrency", "usd");
        await Page.SelectOptionAsync("form[action$='/withdrawal-provider'] #PaymentMethod", "ON_CHAIN");

        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Save Settings" }).ClickAsync();

        var statusText = await (await FindAlertMessageAsync(StatusMessageModel.StatusSeverity.Success)).TextContentAsync();
        Assert.Contains("Withdrawal Provider settings saved.", statusText);

        Assert.Equal("BTCUSD", await Page.InputValueAsync("#Ticker"));
        Assert.Equal("USD", await Page.InputValueAsync("#FiatCurrency"));
        Assert.Equal("ON_CHAIN", await Page.InputValueAsync("form[action$='/withdrawal-provider'] #PaymentMethod"));
    }

    [Fact]
    public async Task CreateOrderShowsValidationErrorsForInvalidInputAsync()
    {
        var user = await CreateAndLoginAdminAsync();

        await GoToUrl($"/plugins/{user.StoreId}/withdrawal-provider");
        var createOrderForm = Page.Locator("form[action$='create-order']");

        await createOrderForm.Locator("#SourceAmountSats").FillAsync("0");
        await createOrderForm.Locator("#IpAddress").FillAsync(string.Empty);
        await createOrderForm.Locator("button[type='submit']").ClickAsync();

        var amountError = createOrderForm.Locator(".text-danger", new LocatorLocatorOptions
        {
            HasTextString = "The field Source Amount (sats) must be between 1 and 9,223,372,036,854,775,807."
        });
        var ipError = createOrderForm.Locator(".text-danger", new LocatorLocatorOptions
        {
            HasTextString = "The IP Address field is required."
        });

        Assert.True(await amountError.IsVisibleAsync());
        Assert.True(await ipError.IsVisibleAsync());
    }

    private async Task<TestAccount> CreateAndLoginAdminAsync()
    {
        await InitializePlaywright(ServerTester);
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin();

        await GoToUrl("/login");
        await LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
        return user;
    }
}
