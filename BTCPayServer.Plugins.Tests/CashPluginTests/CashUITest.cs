using NUnit.Framework;
using Xunit.Abstractions;

namespace BTCPayServer.Plugins.Tests.CashPluginTests;


[TestFixture]
public class CashUITest : PlaywrightBaseTest
{
    public CashUITest(ITestOutputHelper helper) : base(helper){ }


    [Xunit.Fact]
    public async Task EnableCashPaymentTest()
    {
        string testDir = Path.Combine(Directory.GetCurrentDirectory(), "EnableCashPaymentTest");
        using var p = CreateServerTester(testDir);
        await p.StartAsync();
        var storeId = await InitializeAsync(p.PayTester.ServerUri);

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
}
