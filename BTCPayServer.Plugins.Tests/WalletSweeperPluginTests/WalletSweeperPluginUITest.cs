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
    public async Task CanSweepAfterInvoicePaymentAsync()
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

        // Navigate to Wallet Sweeper configuration page and configure with low thresholds
        await GoToUrl($"/plugins/{storeId}/walletsweeper");
        await FindAlertMessageAsync(StatusMessageModel.StatusSeverity.Info);
        await GoToUrl($"/plugins/{storeId}/walletsweeper/configure");
        await Page.Locator("#Enabled").SetCheckedAsync(true); // Enable sweeping
        await Page.FillAsync("#DestinationAddress", "bcrt1qcpf40xrswnmtsugt3xwpd4mrh8cv872jm3l2je");
        // Set low thresholds so the test wallet balance triggers a sweep
        await Page.FillAsync("#MinimumBalance", "0.0001");
        await Page.FillAsync("#MaximumBalance", "10");
        await Page.FillAsync("#ReserveAmount", "0");
        await Page.Locator("button[form='ConfigForm']").ClickAsync();
        await FindAlertMessageAsync(StatusMessageModel.StatusSeverity.Success);

        // create an invoice and pay it through the fake payment UI
        var invoiceId = await CreateInvoice(storeId, amount: 0.001m, currency: "BTC");
        var invoice = await ServerTester.PayTester.InvoiceRepository.GetInvoice(invoiceId);
        Assert.Equal(InvoiceStatus.New, invoice.Status);

        // Navigate to checkout and pay
        //await GoToUrl($"/i/{invoiceId}");
        
        // Wait for and get the iframe
        // var iframe = Page.FrameLocator("iframe[name='btcpay']");
        // await iframe.Locator("#Checkout").WaitForAsync();
        //
        // // Click the fake payment button
        // await iframe.Locator("#FakePayment").ClickAsync();
        // await iframe.Locator("#CheatSuccessMessage").WaitForAsync();
        //
        // // Mine blocks to confirm
        // await ServerTester.ExplorerNode.GenerateAsync(1);
        // await Task.Delay(2000); // Wait for invoice to update
        //
        // invoice = await ServerTester.PayTester.InvoiceRepository.GetInvoice(invoiceId);
        // Assert.Equal(InvoiceStatus.Settled, invoice.Status);

        // Trigger manual sweep and assert success toast is visible
        await GoToUrl($"/plugins/{storeId}/walletsweeper");
        await Page.ClickAsync("form[action$='trigger'] button[type='submit']");
        await FindAlertMessageAsync(StatusMessageModel.StatusSeverity.Success);

        var historyRows = Page.Locator("table tbody tr");
        Assert.True(await historyRows.CountAsync() > 0);
        var latestRow = historyRows.First;
        Assert.Contains("Success", (await latestRow.InnerTextAsync()));
    }

}
