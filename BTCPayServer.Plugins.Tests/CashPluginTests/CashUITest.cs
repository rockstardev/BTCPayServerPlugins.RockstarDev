using BTCPayServer.Tests;
using NUnit.Framework;
using Xunit.Abstractions;

namespace BTCPayServer.Plugins.Tests.CashPluginTests;


[TestFixture]
public class CashUITest : PlaywrightBaseTest, IAsyncDisposable
{
    private string _testDir;
    private ServerTester _serverTester;
    public CashUITest(ITestOutputHelper helper) : base(helper)
    {
        _testDir = Path.Combine(Directory.GetCurrentDirectory(), "CashPaymentTests");
    }


    [Xunit.Fact]
    public async Task EnableCashPaymentTest()
    {
        _serverTester = CreateServerTester(_testDir);
        await _serverTester.StartAsync();
        var storeId = await InitializeAsync(_serverTester.PayTester.ServerUri);

        await GoToUrl($"/stores/{storeId}/cash");

        var checkboxSelector = "input#Enabled";
        var checkBox = await Page.QuerySelectorAsync(checkboxSelector);
        Assert.NotNull(checkBox);

        bool isChecked = await checkBox.IsCheckedAsync();
        if (!isChecked)
            await checkBox.CheckAsync();

        await Page.Locator("input#Submit").ClickAsync();

        checkBox = await Page.QuerySelectorAsync(checkboxSelector);
        Assert.NotNull(checkBox);
        Assert.True(await checkBox.IsCheckedAsync());
    }


    /*[Xunit.Fact]
    public async Task DisableCashPaymentTest()
    {
        _serverTester = CreateServerTester(_testDir);
        await _serverTester.StartAsync();
        var storeId = await InitializeAsync(_serverTester.PayTester.ServerUri);

        await GoToUrl($"/stores/{storeId}/cash");

        var checkboxSelector = "input#Enabled";
        var checkBox = await Page.QuerySelectorAsync(checkboxSelector);
        Assert.NotNull(checkBox);

        bool isDisabled = !await checkBox.IsCheckedAsync();
        if (!isDisabled)
            await checkBox.CheckAsync();

        await Page.Locator("input#Submit").ClickAsync();

        checkBox = await Page.QuerySelectorAsync(checkboxSelector);
        Assert.NotNull(checkBox);
        Assert.False(await checkBox.IsCheckedAsync());
    }*/

    public async ValueTask DisposeAsync()
    {
        if (Page != null)
            await Page.CloseAsync();

        if (Browser != null)
            await Browser.CloseAsync();

        Playwright?.Dispose();
    }
}
