using BTCPayServer.Tests;
using Xunit;
using Xunit.Abstractions;
using static BTCPayServer.Plugins.Tests.CashPluginUITest;

namespace BTCPayServer.Plugins.Tests;

public class CashPluginUITest : PlaywrightBaseTest, IClassFixture<CashPluginServerTesterFixture>
{
    private readonly CashPluginServerTesterFixture _fixture;

    public CashPluginUITest(CashPluginServerTesterFixture fixture, ITestOutputHelper helper) : base(helper)
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
        await InitializePlaywright(ServerTester.PayTester.ServerUri);
        await InitializeBTCPayServer();

        await GoToUrl($"/stores/{StoreId}/cash");
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
        await InitializePlaywright(ServerTester.PayTester.ServerUri);
        await InitializeBTCPayServer();

        await GoToUrl($"/stores/{StoreId}/cash");
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
        var invoiceId = await CreateInvoice(0.001m, "BTC", "a@x.com");
        var invoice = await ServerTester.PayTester.InvoiceRepository.GetInvoice(invoiceId);
        await GoToUrl($"tests/index.html?invoice={invoiceId}");
        await Page.WaitForSelectorAsync("iframe[name='btcpay']");
        var frameElement = await Page.QuerySelectorAsync("iframe[name='btcpay']");
        Assert.NotNull(frameElement);
        var frame = await frameElement.ContentFrameAsync();
        Assert.NotNull(frame);
        await frame.WaitForSelectorAsync("#Checkout");
        var elements = await frame.QuerySelectorAllAsync(".payment-method");
        Assert.Equal(2, elements.Count);
        var activeMethod = await frame.Locator(".payment-method.active").TextContentAsync();
        Assert.Contains("Bitcoin", activeMethod);
        var secondMethod = await frame.Locator(".payment-method").Nth(1).InnerTextAsync();
        Assert.Contains("Cash", secondMethod);
        await frame.Locator(".payment-method").Nth(1).ClickAsync();

        await frame.Locator("#cash-payment").ClickAsync();
        var closeButton = await frame.WaitForSelectorAsync("#close");
        Assert.NotNull(closeButton);
        Assert.True(await closeButton.IsVisibleAsync());
        Assert.Equal(new Uri(ServerTester.PayTester.ServerUri, $"tests/index.html?invoice={invoiceId}").ToString(), Page.Url);
    }

    [Fact]
    public async Task DisableCashPaymentTest()
    {
        await InitializePlaywright(ServerTester.PayTester.ServerUri);
        await InitializeBTCPayServer();

        await GoToUrl($"/stores/{StoreId}/cash");
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


    public class CashPluginServerTesterFixture : IDisposable
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
                var testDir = Path.Combine(Directory.GetCurrentDirectory(), "CashPluginUITest");
                ServerTester = testInstance.CreateServerTester(testDir, true);
                ServerTester.StartAsync().GetAwaiter().GetResult();
            }
        }
    }
}
