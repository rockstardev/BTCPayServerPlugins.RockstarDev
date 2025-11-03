using BTCPayServer.Abstractions.Models;
using BTCPayServer.Tests;
using Xunit.Abstractions;
using Xunit;
using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Tests;

// This test class runs in the "Standalone Tests" collection with a separate database
[Collection("Standalone Tests")]
[Trait("Category", "PlaywrightUITest")]
public class TransactionCounterPluginUITestStandalone : PlaywrightBaseTest
{
    private readonly StandalonePluginTestFixture _fixture;

    public TransactionCounterPluginUITestStandalone(StandalonePluginTestFixture fixture, ITestOutputHelper helper) : base(helper)
    {
        _fixture = fixture;
        if (_fixture.ServerTester == null) _fixture.Initialize(this);
        ServerTester = _fixture.ServerTester;
    }

    public ServerTester ServerTester { get; }


    [Fact]
    public async Task EnableTransactionCounterTest()
    {
        await InitializePlaywright(ServerTester);
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin(true);
        await GoToUrl("/login");
        await LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
        await GoToUrl($"/server/stores/counter");
        var checkboxSelector = "input#Enabled";
        var checkBox = await Page.QuerySelectorAsync(checkboxSelector);
        Assert.NotNull(checkBox);
        var isChecked = await checkBox.IsCheckedAsync();
        if (!isChecked)
            await checkBox.CheckAsync();

        await SaveTransactionCounterSuccessMessage();
        checkBox = await Page.QuerySelectorAsync(checkboxSelector);
        Assert.NotNull(checkBox);
    }



    [Fact]
    public async Task TransactionCounterPublicUrlTest()
    {
        await InitializePlaywright(ServerTester);
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin(true);
        await GoToUrl("/login");
        await LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
        await GoToUrl($"/server/stores/counter");
        var checkboxSelector = "input#Enabled";
        var checkBox = await Page.QuerySelectorAsync(checkboxSelector);
        Assert.NotNull(checkBox);
        var isChecked = await checkBox.IsCheckedAsync();
        if (!isChecked)
            await checkBox.CheckAsync();

        await SaveTransactionCounterSuccessMessage();
        checkBox = await Page.QuerySelectorAsync(checkboxSelector);
        Assert.NotNull(checkBox);
        var links = Page.Locator(".info-note a");

        var popup1Task = Page.WaitForPopupAsync();
        await links.Nth(0).ClickAsync();
        var popup1 = await popup1Task;
        await popup1.WaitForLoadStateAsync();
        Assert.Contains("/txcounter/html", popup1.Url);
        await popup1.CloseAsync();

        var popup2Task = Page.WaitForPopupAsync();
        await links.Nth(1).ClickAsync();
        var popup2 = await popup2Task;
        await popup2.WaitForLoadStateAsync();
        Assert.Contains("/txcounter/api", popup2.Url);
        await popup2.CloseAsync();
    }


    [Fact]
    public async Task TransactionCounterPasswordSetForPublicUrlTest()
    {
        await InitializePlaywright(ServerTester);

        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin(true);
        await GoToUrl("/login");
        await LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
        await GoToUrl($"/server/stores/counter");
        var checkboxSelector = "input#Enabled";
        var testPassword = "0000";
        var checkBox = await Page.QuerySelectorAsync(checkboxSelector);
        Assert.NotNull(checkBox);
        var isChecked = await checkBox.IsCheckedAsync();
        if (!isChecked)
            await checkBox.CheckAsync();

        await Page.Locator("#Password").FillAsync(testPassword);
        await SaveTransactionCounterSuccessMessage();
        checkBox = await Page.QuerySelectorAsync(checkboxSelector);
        Assert.NotNull(checkBox);
        var links = Page.Locator(".info-note a");

        var popup1Task = Page.WaitForPopupAsync();
        await links.Nth(0).ClickAsync();
        var popup1 = await popup1Task;
        await popup1.WaitForLoadStateAsync();
        var passwordInputLinkeOne = await popup1.QuerySelectorAsync("input[name='password']");
        Assert.NotNull(passwordInputLinkeOne);
        await passwordInputLinkeOne.FillAsync(testPassword);
        await popup1.Locator("button[type='submit']").ClickAsync();
        Assert.Contains("/txcounter/html", popup1.Url);
        Assert.Contains($"password={testPassword}", popup1.Url);
        await popup1.CloseAsync();

        var popup2Task = Page.WaitForPopupAsync();
        await links.Nth(1).ClickAsync();
        var popup2 = await popup2Task;
        await popup2.WaitForLoadStateAsync();
        var passwordInputLinkTwo = await popup2.QuerySelectorAsync("input[name='password']");
        Assert.NotNull(passwordInputLinkTwo);
        await passwordInputLinkTwo.FillAsync(testPassword);
        await popup2.Locator("button[type='submit']").ClickAsync();
        Assert.Contains("/txcounter/api", popup2.Url);
        Assert.Contains($"password={testPassword}", popup2.Url);
        await popup2.CloseAsync();
    }


    [Fact]
    public async Task TransactionCounterCustomTransactionTest()
    {
        await InitializePlaywright(ServerTester);
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin(true);
        await GoToUrl("/login");
        await LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
        await GoToUrl($"/server/stores/counter");
        var checkboxSelector = "input#Enabled";
        var checkBox = await Page.QuerySelectorAsync(checkboxSelector);
        Assert.NotNull(checkBox);
        var isChecked = await checkBox.IsCheckedAsync();
        if (!isChecked)
            await checkBox.CheckAsync();

        var includeTransactionVolumeCheckBox = await Page.QuerySelectorAsync("input#IncludeTransactionVolume");
        Assert.NotNull(includeTransactionVolumeCheckBox);
        isChecked = await includeTransactionVolumeCheckBox.IsCheckedAsync();
        if (!isChecked)
            await includeTransactionVolumeCheckBox.CheckAsync();
            
        var now = DateTime.UtcNow;
        var start = now.AddHours(-4);
        var end = now.AddHours(-1);
        var count = 100;
        var extraTransactions = new[]
        {
                new {
                    source = "test",
                    start = start.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    end = end.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    amount = 21,
                    currency = "NGN",
                    count
                }
            };
        var json = JsonConvert.SerializeObject(extraTransactions, Formatting.Indented);
        await Page.Locator("#extra-transactions-json").FillAsync(json);
        await Page.Locator("#Password").FillAsync("");
        await SaveTransactionCounterSuccessMessage();
        checkBox = await Page.QuerySelectorAsync(checkboxSelector);
        Assert.NotNull(checkBox);
        var links = Page.Locator(".info-note a");

        var popup1Task = Page.WaitForPopupAsync();
        await links.Nth(1).ClickAsync();
        var popup1 = await popup1Task;
        await popup1.WaitForLoadStateAsync();
        Assert.Contains("/txcounter/api", popup1.Url);
        var jsonText = await popup1.Locator("body").InnerTextAsync();
        jsonText = jsonText.Trim();
        Assert.StartsWith("{", jsonText);
        Assert.EndsWith("}", jsonText);
        var parsed = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonText);
        Assert.NotNull(parsed);
        Assert.True(parsed.TryGetValue("count", out var countObj));
        Assert.Equal(count, Convert.ToInt32(countObj));
        Assert.True(parsed.TryGetValue("volumeByCurrency", out var volumeObj));
        var volumeByCurrency = JsonConvert.DeserializeObject<Dictionary<string, decimal>>(volumeObj.ToString() ?? "{}");
        Assert.NotNull(volumeByCurrency);
        Assert.True(volumeByCurrency.TryGetValue("NGN", out decimal eurVolume));
        Assert.Equal(21, eurVolume);
        await popup1.CloseAsync();
    }

    private async Task SaveTransactionCounterSuccessMessage()
    {
        var expectedSeverity = StatusMessageModel.StatusSeverity.Success;
        await Page.Locator("#page-primary").ClickAsync();
        var invoiceCreationStatusText = (await FindAlertMessageAsync(expectedSeverity)).TextContentAsync();
        Assert.Equal("Plugin counter configuration updated successfully", (await invoiceCreationStatusText)?.Trim());
    }
}
