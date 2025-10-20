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
        // Set fast sweep interval for testing (1 second) - MUST be before server starts
        Environment.SetEnvironmentVariable("BTCPAY_WALLETSWEEPER_INTERVAL", "1");
        
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
}
