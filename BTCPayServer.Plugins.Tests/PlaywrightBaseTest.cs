using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Tests;
using BTCPayServer.Views.Stores;
using Microsoft.Playwright;
using NBitcoin;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Plugins.Tests;

public class PlaywrightBaseTest : UnitTestBase
{
    public PlaywrightBaseTest(ITestOutputHelper helper) : base(helper)
    {
    }

    public ServerTester Server { get; set; }
    public WalletId WalletId { get; set; }
    public IPlaywright Playwright { get; private set; }
    public IBrowser Browser { get; private set; }
    public IPage Page { get; private set; }
    public string StoreName { get; set; }
    public Uri ServerUri { get; private set; }
    public string Password { get; private set; }
    public string StoreId { get; private set; }
    public bool IsAdmin { get; private set; }

    private bool _isInitialized = false;

    string CreatedUser;


    public async Task<string> InitializeAsync(Uri uri)
    {
        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();

        Browser = await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = false, // Set to true for CI/automated environments
            SlowMo = 50 // Add slight delay between actions to improve stability
        });

        var context = await Browser.NewContextAsync();
        Page = await context.NewPageAsync();
        ServerUri = uri;
        TestLogs.LogInformation($"Playwright: Browsing to {ServerUri}");

        await Page.GotoAsync(ServerUri.ToString());
        if (!_isInitialized)
        {
            await InitializeBTCPayServer();
            _isInitialized = true;
        }
        return StoreId;
    }

    public async Task GoToUrl(string relativeUrl)
    {
        await Page.GotoAsync(Link(relativeUrl));
    }


    public string Link(string relativeLink)
    {
        return ServerUri.AbsoluteUri.WithoutEndingSlash() + relativeLink.WithStartingSlash();
    }

    public async Task<string> RegisterNewUser(bool isAdmin = false)
    {
        var usr = RandomUtils.GetUInt256().ToString().Substring(64 - 20) + "@a.com";
        await Page.FillAsync("#Email", usr);
        await Page.FillAsync("#Password", "123456");
        await Page.FillAsync("#ConfirmPassword", "123456");
        if (isAdmin)
            await Page.ClickAsync("#IsAdmin");

        await Page.ClickAsync("#RegisterButton");
        CreatedUser = usr;
        Password = "123456";
        IsAdmin = isAdmin;
        return usr;
    }

    public async Task GoToStore(StoreNavPages storeNavPage = StoreNavPages.General)
    {
        await GoToStore(null, storeNavPage);
    }

    public async Task GoToStore(string storeId, StoreNavPages storeNavPage = StoreNavPages.General)
    {
        if (storeId is not null)
        {
            await GoToUrl($"/stores/{storeId}/");
            StoreId = storeId;
            if (WalletId != null)
                WalletId = new WalletId(storeId, WalletId.CryptoCode);
        }
        if (storeNavPage != StoreNavPages.General)
        {
            await Page.Locator($"#StoreNav-{StoreNavPages.General}").ClickAsync();
        }
        await Page.Locator($"#StoreNav-{storeNavPage}").ClickAsync();
    }

    public async Task<(string storeName, string storeId)> CreateNewStoreAsync(bool keepId = true)
    {
        if (await Page.Locator("#StoreSelectorToggle").IsVisibleAsync())
        {
            await Page.Locator("#StoreSelectorToggle").ClickAsync();
        }
        await GoToUrl("/stores/create");
        var name = "Store" + RandomUtils.GetUInt64();
        TestLogs.LogInformation($"Created store {name}");
        await Page.FillAsync("#Name", name);

        var selectedOption = await Page.Locator("#PreferredExchange option:checked").TextContentAsync();
        Assert.Equal("Recommendation (Kraken)", selectedOption.Trim());
        await Page.Locator("#PreferredExchange").SelectOptionAsync(new SelectOptionValue { Label = "CoinGecko" });
        await Page.ClickAsync("#Create");
        await Page.ClickAsync("#StoreNav-General");
        var storeId = await Page.InputValueAsync("#Id");
        if (keepId)
            StoreId = storeId;

        return (name, storeId);
    }


    public async Task GoToWalletSettingsAsync(string cryptoCode = "BTC")
    {
        await Page.ClickAsync($"#StoreNav-Wallet{cryptoCode}");
        var walletNavSettings = Page.Locator("#WalletNav-Settings");
        if (await walletNavSettings.CountAsync() > 0)
        {
            await walletNavSettings.ClickAsync();
        }
    }


    /// <summary>
    /// Assume to be in store's settings
    /// </summary>
    /// <param name="cryptoCode"></param>
    /// <param name="derivationScheme"></param>
    public async Task AddDerivationScheme(string cryptoCode = "BTC", string derivationScheme = "tpubD6NzVbkrYhZ4XxNXjYTcRujMc8z8734diCthtFGgDMimbG5hUsKBuSTCuUyxWL7YwP7R4A5StMTRQiZnb6vE4pdHWPgy9hbiHuVJfBMumUu-[legacy]")
    {
        if (!(await Page.ContentAsync()).Contains($"Setup {cryptoCode} Wallet"))
        {
            await GoToWalletSettingsAsync(cryptoCode);
        }

        await Page.Locator("#ImportWalletOptionsLink").ClickAsync();
        await Page.Locator("#ImportXpubLink").ClickAsync();
        await Page.FillAsync("#DerivationScheme", derivationScheme);
        await Page.Locator("#Continue").ClickAsync();
        await Page.Locator("#Confirm").ClickAsync();
        await FindAlertMessageAsync();
    }


    public async Task GoToLightningSettingsAsync(string cryptoCode = "BTC")
    {
        await Page.Locator($"#StoreNav-Lightning{cryptoCode}").ClickAsync();
        // if Lightning is already set up we need to navigate to the settings
        if (await Page.Locator("#StoreNav-LightningSettings").CountAsync() > 0)
        {
            await Page.Locator("#StoreNav-LightningSettings").ClickAsync();
        }
    }


    public async Task<ILocator> FindAlertMessageAsync(StatusMessageModel.StatusSeverity severity = StatusMessageModel.StatusSeverity.Success)
    {
        return await FindAlertMessageAsync(new[] { severity });
    }

    public async Task<ILocator> FindAlertMessageAsync(params StatusMessageModel.StatusSeverity[] severity)
    {
        var className = string.Join(", ", severity.Select(statusSeverity => $".alert-{StatusMessageModel.ToString(statusSeverity)}"));
        var locator = Page.Locator(className);
        try
        {
            await locator.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
            var visibleElements = await locator.AllAsync();
            var visibleElement = visibleElements.FirstOrDefault(el => el.IsVisibleAsync().GetAwaiter().GetResult());

            if (visibleElement != null)
                return Page.Locator(className).First;

            return locator.First;
        }
        catch (TimeoutException)
        {
            // If no element found, throw exception
            throw new TimeoutException($"Unable to find {className}");
        }
    }


    private async Task InitializeBTCPayServer()
    {
        await GoToUrl("/register");
        await RegisterNewUser(true);
        await CreateNewStoreAsync();
        await GoToStore();
        await AddDerivationScheme();
    }


    public async ValueTask DisposeAsync()
    {
        if (Page != null)
            await Page.CloseAsync();

        if (Browser != null)
            await Browser.CloseAsync();

        Playwright?.Dispose();
    }
}