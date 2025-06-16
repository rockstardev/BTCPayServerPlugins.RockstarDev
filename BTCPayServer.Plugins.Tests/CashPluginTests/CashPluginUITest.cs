using BTCPayServer.Tests;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Plugins.Tests;

[Collection("Plugin Tests")]
public class CashPluginUITest : PlaywrightBaseTest
{
    private readonly SharedPluginTestFixture _fixture;

    public CashPluginUITest(SharedPluginTestFixture fixture, ITestOutputHelper helper) : base(helper)
    {
        _fixture = fixture;
        if (_fixture.ServerTester == null) _fixture.Initialize(this);
        ServerTester = _fixture.ServerTester;
    }

    public ServerTester ServerTester { get; }

    public string TestDir { get; private set; }

    [Fact]
    public async Task EnableCashPaymentTest()
    {
        await InitializePlaywright(ServerTester);
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin(true);
        await GoToUrl($"/login");
        await LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
        await GoToUrl($"/stores/{user.StoreId}/cash");
        var checkboxSelector = "input#Enabled";
        var checkBox = await Page.QuerySelectorAsync(checkboxSelector);
        Assert.NotNull(checkBox);
        var isChecked = await checkBox.IsCheckedAsync();
        if (!isChecked)
            await checkBox.CheckAsync();

        await Page.Locator("input#Submit").ClickAsync();
        checkBox = await Page.QuerySelectorAsync(checkboxSelector);
        Assert.NotNull(checkBox);
        Assert.True(await checkBox.IsCheckedAsync());
    }

    [Fact]
    public async Task CanUseCheckoutAsModal()
    {
        await InitializePlaywright(ServerTester);
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin(true);
        await GoToUrl("/login");
        await LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
        await GoToUrl($"/stores/{user.StoreId}/cash");
        var checkboxSelector = "input#Enabled";
        var checkBox = await Page.QuerySelectorAsync(checkboxSelector);
        Assert.NotNull(checkBox);
        var isChecked = await checkBox.IsCheckedAsync();
        if (!isChecked)
            await checkBox.CheckAsync();

        await Page.Locator("input#Submit").ClickAsync();
        checkBox = await Page.QuerySelectorAsync(checkboxSelector);
        Assert.NotNull(checkBox);
        Assert.True(await checkBox.IsCheckedAsync());
        var invoiceId = await CreateInvoice(user.StoreId, 0.001m, "BTC", "a@x.com");
        var invoice = await ServerTester.PayTester.InvoiceRepository.GetInvoice(invoiceId);
        await GoToUrl($"tests/index.html?invoice={invoiceId}");
        await Page.WaitForSelectorAsync("iframe[name='btcpay']");
        var frameElement = await Page.QuerySelectorAsync("iframe[name='btcpay']");
        Assert.NotNull(frameElement);
        var frame = await frameElement.ContentFrameAsync();
        Assert.NotNull(frame);
        await frame.WaitForSelectorAsync("#Checkout");
        await frame.Locator("#cash-payment").ClickAsync();
        Assert.Equal(new Uri(ServerTester.PayTester.ServerUri, $"tests/index.html?invoice={invoiceId}").ToString(), Page.Url);
    }

    [Fact]
    public async Task DisableCashPaymentTest()
    {
        await InitializePlaywright(ServerTester);
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin(true);
        await GoToUrl($"/login");
        await LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
        await GoToUrl($"/stores/{user.StoreId}/cash");
        var checkboxSelector = "input#Enabled";
        var checkBox = await Page.QuerySelectorAsync(checkboxSelector);
        Assert.NotNull(checkBox);
        var isDisabled = !await checkBox.IsCheckedAsync();
        if (!isDisabled)
            await checkBox.CheckAsync();

        await Page.Locator("input#Submit").ClickAsync();
        checkBox = await Page.QuerySelectorAsync(checkboxSelector);
        Assert.NotNull(checkBox);
        Assert.False(await checkBox.IsCheckedAsync());
    }
}
