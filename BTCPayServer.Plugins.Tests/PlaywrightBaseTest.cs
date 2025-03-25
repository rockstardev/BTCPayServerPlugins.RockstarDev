using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Lightning;
using BTCPayServer.Lightning.CLightning;
using BTCPayServer.Tests;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NBitcoin;
using OpenQA.Selenium;

namespace BTCPayServer.Plugins.Tests;

public class PlaywrightBaseTest : IAsyncDisposable
{

    public WalletId WalletId { get; set; }
    public IPlaywright Playwright { get; private set; }
    public IBrowser Browser { get; private set; }
    public IPage Page { get; private set; }
    public string AdminEmail { get; set; }
    public string AdminPassword { get; set; }
    public string StoreName { get; set; }
    public Uri ServerUri { get; private set; }
    public string Password { get; private set; }
    public string StoreId { get; private set; }
    public bool IsAdmin { get; private set; }

    private bool _isInitialized = false;

    string CreatedUser;

    public async Task StartAsync()
    {
        var builder = new ConfigurationBuilder().AddUserSecrets<BTCPayServerTester>();
        var configuration = builder.Build();

        AdminEmail = "admin@example.com";
        AdminPassword = "password";
        StoreName = "Test Store";
        
        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        
        Browser = await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = false, // Set to true for CI/automated environments
            SlowMo = 50 // Add slight delay between actions to improve stability
        });

        var context = await Browser.NewContextAsync();
        Page = await context.NewPageAsync();

        // Navigate to BTCPay Server
        ServerUri = new Uri("http://localhost:14142"); // Adjust as needed
        await Page.GotoAsync(ServerUri.ToString());

        // Ensure initialization happens only once
        if (!_isInitialized)
        {
            await InitializeBTCPayServer();
            _isInitialized = true;
        }
    }

    public async Task GoToUrl(string relativeUrl)
    {
        await Page.GotoAsync(Link(relativeUrl));
    }

    public string Link(string relativeLink)
    {
        return ServerUri.AbsoluteUri.WithoutEndingSlash() + relativeLink.WithStartingSlash();
    }

    public async Task GoToRegister()
    {
        await GoToUrl("/register");
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
        await Page.WaitForSelectorAsync("body:has-no-text('error')", new PageWaitForSelectorOptions { Timeout = 5000 });
        CreatedUser = usr;
        Password = "123456";
        IsAdmin = isAdmin;
        return usr;
    }

    public async Task<(string storeName, string storeId)> CreateNewStoreAsync(bool keepId = true)
    {
        try
        {
            var storeSelectorToggle = Page.Locator("#StoreSelectorToggle");
            if (await storeSelectorToggle.CountAsync() > 0)
            {
                await storeSelectorToggle.ClickAsync();
            }
        }
        catch { }

        await GoToUrl("/stores/create");
        var name = "Store" + RandomUtils.GetUInt64();
        await Page.FillAsync("#Name", name);
        var preferredExchangeSelect = Page.Locator("#PreferredExchange");
        var selectedOption = await preferredExchangeSelect.SelectOptionAsync("CoinGecko");
        // Assert.Equal("Recommendation (Kraken)", rateSource.SelectedOption.Text);
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


    public async Task<Mnemonic> GenerateWalletAsync(string cryptoCode = "BTC", string seed = "", bool? importkeys = null, bool isHotWallet = false, ScriptPubKeyType format = ScriptPubKeyType.Segwit)
    {
        var isImport = !string.IsNullOrEmpty(seed);
        await GoToWalletSettingsAsync(cryptoCode);
        var changeWalletLink = Page.Locator("#ChangeWalletLink");
        // Replace previous wallet case
        if (await changeWalletLink.CountAsync() > 0)
        {
            await Page.Locator("#ActionsDropdownToggle").ClickAsync();
            await Page.Locator("#ChangeWalletLink").ClickAsync();
            await Page.FillAsync("#ConfirmInput", "REPLACE");
            await Page.Locator("#ConfirmContinue").ClickAsync();
        }

        if (isImport)
        {
            await Page.Locator("#ImportWalletOptionsLink").ClickAsync();
            await Page.Locator("#ImportSeedLink").ClickAsync();
            await Page.FillAsync("#ExistingMnemonic", seed);
            await Page.Locator("#SavePrivateKeys").SetCheckedAsync(isHotWallet);
        }
        else
        {
            var option = isHotWallet ? "Hotwallet" : "Watchonly";
            await Page.Locator("#GenerateWalletLink").ClickAsync();
            await Page.Locator($"#Generate{option}Link").ClickAsync();
        }

        await Page.Locator("#ScriptPubKeyType").ClickAsync();
        await Page.Locator($"#ScriptPubKeyType option[value='{format}']").ClickAsync();



        await Page.Locator("#AdvancedSettings [data-toggle='collapse']").ClickAsync();
        // await page.GetByRole("button", new { name = "Advanced Settings" }).ClickAsync();

        if (importkeys is bool v)
            await Page.Locator("#ImportKeysToRPC").SetCheckedAsync(v);
        await Page.Locator("#Continue").ClickAsync();

        if (isImport)
        {
            // Confirm addresses
            await Page.Locator("#Confirm").ClickAsync();
        }
        else
        {
            // Seed backup
            await FindAlertMessageAsync();
            if (string.IsNullOrEmpty(seed))
            {
                seed = await Page.Locator("#RecoveryPhrase").First.GetAttributeAsync("data-mnemonic");
            }

            // Confirm seed backup
            await Page.Locator("#confirm").ClickAsync();
            await Page.Locator("#submit").ClickAsync();
        }

        WalletId = new WalletId(StoreId, cryptoCode);
        return new Mnemonic(seed);
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
        try
        {
            await GoToRegister();
            await RegisterNewUser();
            // Try to log in or check if initial setup is required
            await HandleInitialSetup();

            // Create store if it doesn't exist
            await EnsureStoreExists();

            // Navigate to plugin section
            await NavigateToPlugin();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Initialization error: {ex.Message}");
            throw;
        }
    }

    private async Task HandleInitialSetup()
    {
        // Check if login is required or first-time setup
        try
        {
            // Try to log in
            await Page.GotoAsync(Link("/login"));

            // Check if login page is visible
            await Page.WaitForSelectorAsync("#Email");

            // Perform login
            await Page.FillAsync("#Email", AdminEmail);
            await Page.FillAsync("#Password", AdminPassword);
            await Page.ClickAsync("button[type='submit']");

            // Wait for login to complete
            await Page.WaitForURLAsync("**/dashboard");
        }
        catch
        {
            // If login fails, might need first-time setup
            await PerformFirstTimeSetup();
        }
    }

    private async Task PerformFirstTimeSetup()
    {
        // Implement first-time setup logic
        // This might include:
        // - Creating initial admin account
        // - Setting up initial configuration
        await Page.FillAsync("#Email", AdminEmail);
        await Page.FillAsync("#Password", AdminPassword);
        await Page.FillAsync("#ConfirmPassword", AdminPassword);
        await Page.ClickAsync("button[type='submit']");

        // Additional setup steps as needed
    }

    private async Task EnsureStoreExists()
    {
        // Navigate to stores
        await Page.GotoAsync($"{ServerUri}/stores");

        // Check if store exists, create if not
        var storeLocator = $"text={StoreName}";
        try
        {
            // Try to find existing store
            await Page.WaitForSelectorAsync(storeLocator, new PageWaitForSelectorOptions { Timeout = 3000 });
        }
        catch
        {
            // Store doesn't exist, create new store
            await Page.ClickAsync("text=Create a new store");
            await Page.FillAsync("#StoreName", StoreName);
            await Page.ClickAsync("button[type='submit']");
        }
    }

    private async Task NavigateToPlugin()
    {
        // Navigate to the specific plugin section
        // Adjust the selector based on your plugin's location in the navigation
        await Page.ClickAsync("text=Plugins"); // Adjust this selector
        await Page.WaitForLoadStateAsync();
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