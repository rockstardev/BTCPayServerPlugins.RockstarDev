using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Plugins.Tests;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Tests;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;
using NBitcoin;
using BTCPayServer;
using BTCPayServer.Abstractions.Models;

namespace BTCPayServer.Plugins.Tests;

[Collection("Plugin Tests")]
[Trait("Category", "PlaywrightUITest")]
public class WalletSweeperPluginUITest : PlaywrightBaseTest
{
    private readonly SharedPluginTestFixture _fixture;

    public WalletSweeperPluginUITest(SharedPluginTestFixture fixture, ITestOutputHelper helper) : base(helper)
    {
        _fixture = fixture;
        if (_fixture.ServerTester == null)
            _fixture.Initialize(this);
        ServerTester = _fixture.ServerTester;
    }

    public ServerTester ServerTester { get; }

    [Fact]
    public async Task CanSweepManuallyAndAutomaticallyAsync()
    {
        await InitializePlaywright(ServerTester);
        var account = ServerTester.NewAccount();
        await account.GrantAccessAsync();
        await account.MakeAdmin(true);

        await GoToUrl("/login");
        await LogIn(account.RegisterDetails.Email, account.RegisterDetails.Password);

        // Go to hot wallet settings and generate an on-chain hot wallet
        await ServerTester.ExplorerNode.GenerateAsync(1);
        var walletId = await account.RegisterDerivationSchemeAsync("BTC", importKeysToNBX: true);
        WalletId = walletId;
        var storeId = account.StoreId;

        // fund the wallet with a couple of UTXOs using cheat mode button
        await GoToUrl($"/wallets/{walletId}");
        await Page.ClickAsync("#WalletNav-Receive");
        await Page.ClickAsync("//button[@value='fill-wallet']");
        await Page.ClickAsync("#CancelWizard");

        // STEP 1: Configure sweep with auto-sweep DISABLED
        await GoToUrl($"/plugins/{storeId}/walletsweeper");
        await FindAlertMessageAsync(StatusMessageModel.StatusSeverity.Info);
        await GoToUrl($"/plugins/{storeId}/walletsweeper/configure");
        await Page.Locator("#Enabled").SetCheckedAsync(false); // Disable auto-sweeping
        await Page.FillAsync("#DestinationAddress", "bcrt1qcpf40xrswnmtsugt3xwpd4mrh8cv872jm3l2je");
        // Set thresholds: Min 0.0001 BTC, Max 2 BTC
        await Page.FillAsync("#MinimumBalance", "0.0001");
        await Page.FillAsync("#MaximumBalance", "2");
        await Page.FillAsync("#ReserveAmount", "0");
        await Page.Locator("button[form='ConfigForm']").ClickAsync();
        await FindAlertMessageAsync(StatusMessageModel.StatusSeverity.Success);

        // STEP 2: Test MANUAL sweep works (even with auto-sweep disabled)
        await GoToUrl($"/plugins/{storeId}/walletsweeper");
        await Page.ClickAsync("form[action$='trigger'] button[type='submit']");
        await FindAlertMessageAsync(StatusMessageModel.StatusSeverity.Success);
        
        // Verify sweep history entry was created
        var historyRows = Page.Locator("table tbody tr");
        Assert.True(await historyRows.CountAsync() > 0);
        var latestRow = historyRows.First;
        Assert.Contains("Success", (await latestRow.InnerTextAsync()));
        TestLogs.LogInformation("✓ Manual sweep succeeded with auto-sweep disabled");

        // STEP 3: Enable automatic sweeping
        await GoToUrl($"/plugins/{storeId}/walletsweeper/configure");
        await Page.Locator("#Enabled").SetCheckedAsync(true); // Enable auto-sweeping
        await Page.Locator("button[form='ConfigForm']").ClickAsync();
        await FindAlertMessageAsync(StatusMessageModel.StatusSeverity.Success);
        TestLogs.LogInformation("✓ Auto-sweep enabled");

        // STEP 4: Fund wallet again to exceed max threshold (2 BTC)
        await GoToUrl($"/wallets/{walletId}");
        await Page.ClickAsync("#WalletNav-Receive");
        await Page.ClickAsync("//button[@value='fill-wallet']"); // Fund again (will add ~1 BTC)
        await Page.ClickAsync("#CancelWizard");
        await ServerTester.ExplorerNode.GenerateAsync(1); // Confirm transaction
        TestLogs.LogInformation("✓ Wallet funded to exceed max threshold");

        // STEP 5: Wait for automatic sweep (should trigger within ~1-2 seconds)
        await Task.Delay(5000); // Wait 5 seconds for automatic sweep
        
        // STEP 6: Verify automatic sweep occurred
        await GoToUrl($"/plugins/{storeId}/walletsweeper");
        historyRows = Page.Locator("table tbody tr");
        var rowCount = await historyRows.CountAsync();
        Assert.True(rowCount >= 2); // Should have at least 2 sweeps (manual + automatic)
        TestLogs.LogInformation($"✓ Automatic sweep succeeded ({rowCount} total sweeps in history)");
    }

    [Fact]
    public async Task CanConfigureColdWalletWithSeedValidationAsync()
    {
        await InitializePlaywright(ServerTester);
        var account = ServerTester.NewAccount();
        await account.GrantAccessAsync();
        await account.MakeAdmin(true);

        await GoToUrl("/login");
        await LogIn(account.RegisterDetails.Email, account.RegisterDetails.Password);

        // Generate a known seed and derive xpub from it
        var mnemonic = new Mnemonic(Wordlist.English, WordCount.Twelve);
        var seedPhrase = mnemonic.ToString();
        var encryptionPassword = "TestPassword123!";
        
        TestLogs.LogInformation($"Generated test seed: {seedPhrase.Substring(0, 20)}...");
        
        // Derive xpub for regtest
        var masterKey = mnemonic.DeriveExtKey();
        var accountKey = masterKey.Derive(new KeyPath("m/84'/1'/0'")); // Regtest native segwit
        var xpub = accountKey.Neuter().ToString(Network.RegTest);
        
        TestLogs.LogInformation($"Derived xpub: {xpub.Substring(0, 20)}...");
        
        // Set up watch-only wallet via UI using the derived xpub
        await ServerTester.ExplorerNode.GenerateAsync(1);
        var storeId = account.StoreId;
        
        // Navigate to wallet setup (same as multisig test)
        await GoToUrl($"/stores/{storeId}");
        await Page.ClickAsync("#StoreNav-WalletBTC");
        await Page.ClickAsync("#ImportWalletOptionsLink");
        await Page.ClickAsync("#ImportXpubLink");
        
        // Enter the xpub as watch-only
        await Page.FillAsync("#DerivationScheme", xpub);
        await Page.ClickAsync("#Continue");
        await Page.ClickAsync("#Confirm");
        
        var walletId = new WalletId(storeId, "BTC");
        WalletId = walletId;
        TestLogs.LogInformation("✓ Watch-only wallet configured with known seed");

        // Fund the wallet
        await GoToUrl($"/wallets/{walletId}");
        await Page.ClickAsync("#WalletNav-Receive");
        await Page.ClickAsync("//button[@value='fill-wallet']");
        await Page.ClickAsync("#CancelWizard");
        await ServerTester.ExplorerNode.GenerateAsync(1);
        TestLogs.LogInformation("✓ Wallet funded");

        // STEP 1: Try to configure without seed - should fail
        await GoToUrl($"/plugins/{storeId}/walletsweeper/configure");
        
        // Verify it shows as cold wallet
        var coldWalletText = await Page.Locator("text=Cold Wallet Detected").CountAsync();
        Assert.True(coldWalletText > 0, "Should show Cold Wallet Detected warning");
        TestLogs.LogInformation("✓ Confirmed wallet is cold/watch-only");

        // Try to save without seed
        await Page.FillAsync("#DestinationAddress", "bcrt1qcpf40xrswnmtsugt3xwpd4mrh8cv872jm3l2je");
        await Page.FillAsync("#MinimumBalance", "0.0001");
        await Page.FillAsync("#MaximumBalance", "2");
        await Page.FillAsync("#ReserveAmount", "0");
        await Page.Locator("button[form='ConfigForm']").ClickAsync();
        
        // Should show validation error for missing seed
        var seedError = Page.Locator("[data-valmsg-for='SeedPhrase'].text-danger");
        var errorText = await seedError.InnerTextAsync();
        Assert.Contains("Seed phrase is required", errorText);
        TestLogs.LogInformation("✓ Correctly rejected configuration without seed phrase");

        // STEP 2: Try with WRONG seed phrase - should fail validation
        var wrongSeed = "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";
        
        await Page.FillAsync("#SeedPhrase", wrongSeed);
        await Page.FillAsync("#SeedPassword", encryptionPassword);
        await Page.FillAsync("#SeedPasswordConfirm", encryptionPassword);
        await Page.Locator("button[form='ConfigForm']").ClickAsync();
        
        // Should show validation error about seed not matching wallet
        errorText = await seedError.InnerTextAsync();
        Assert.Contains("does not match", errorText);
        TestLogs.LogInformation("✓ Correctly rejected wrong seed phrase");

        // STEP 3: Configure with CORRECT seed phrase
        await Page.FillAsync("#SeedPhrase", seedPhrase);
        await Page.FillAsync("#SeedPassword", encryptionPassword);
        await Page.FillAsync("#SeedPasswordConfirm", encryptionPassword);
        await Page.Locator("button[form='ConfigForm']").ClickAsync();
        
        // Wait for page to load and check for success or error
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        
        // Check if there's an error first
        var hasError = await Page.Locator("[data-valmsg-for='SeedPhrase'].text-danger.field-validation-error").IsVisibleAsync();
        if (hasError)
        {
            var validationError = await Page.Locator("[data-valmsg-for='SeedPhrase'].text-danger").InnerTextAsync();
            TestLogs.LogInformation($"Validation error: {validationError}");
            throw new Exception($"Seed validation failed: {validationError}");
        }
        
        await FindAlertMessageAsync(StatusMessageModel.StatusSeverity.Success);
        TestLogs.LogInformation("✓ Configuration saved with correct seed phrase");

        // STEP 4: Verify seed is hidden after save
        await GoToUrl($"/plugins/{storeId}/walletsweeper/configure");
        
        var seedConfiguredText = await Page.Locator("text=Cold Wallet Seed Configured").CountAsync();
        Assert.True(seedConfiguredText > 0, "Should show seed configured message");
        
        var seedInputVisible = await Page.Locator("#SeedPhrase").IsVisibleAsync();
        Assert.False(seedInputVisible, "Seed input should not be visible when already configured");
        TestLogs.LogInformation("✓ Seed phrase is hidden after configuration");

        // STEP 5: Test manual sweep with correct password
        await GoToUrl($"/plugins/{storeId}/walletsweeper");
        
        var triggerButton = Page.Locator("button:has-text('Trigger Sweep')");
        await triggerButton.ClickAsync();
        
        await Page.FillAsync("#seedPassword", encryptionPassword);
        await Page.ClickAsync("form[action$='trigger'] button[type='submit']");
        
        await FindAlertMessageAsync(StatusMessageModel.StatusSeverity.Success);
        TestLogs.LogInformation("✓ Manual sweep succeeded with correct password");
        
        var historyRows = Page.Locator("table tbody tr");
        Assert.True(await historyRows.CountAsync() > 0);
        TestLogs.LogInformation("✓ Sweep history shows successful transaction");

        // STEP 6: Test that wrong password fails
        await Page.ClickAsync("button:has-text('Trigger Sweep')");
        await Page.FillAsync("#seedPassword", "WrongPassword123!");
        await Page.ClickAsync("form[action$='trigger'] button[type='submit']");
        
        await FindAlertMessageAsync(StatusMessageModel.StatusSeverity.Error);
        TestLogs.LogInformation("✓ Manual sweep correctly failed with wrong password");
    }
}
