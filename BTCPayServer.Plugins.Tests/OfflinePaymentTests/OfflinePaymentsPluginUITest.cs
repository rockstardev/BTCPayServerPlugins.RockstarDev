using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client.Models;
using BTCPayServer.Tests;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Plugins.Tests.OfflinePaymentTests;

[Collection("Plugin Tests")]
[Trait("Category", "PlaywrightUITest")]
public class OfflinePaymentsPluginUITest : PlaywrightBaseTest
{
    private readonly SharedPluginTestFixture _fixture;

    public OfflinePaymentsPluginUITest(SharedPluginTestFixture fixture, ITestOutputHelper helper) : base(helper)
    {
        _fixture = fixture;
        if (_fixture.ServerTester == null)
            _fixture.Initialize(this);
        ServerTester = _fixture.ServerTester;
    }

    public ServerTester ServerTester { get; }

    private async Task CreateACHMethod(string storeId)
    {
        await GoToUrl($"/plugins/{storeId}/offline-payments/create");
        await Page.Locator("#MethodId").SelectOptionAsync("ACH");
        await Page.Locator("#DisplayName").FillAsync("ACH Bank Transfer");
        await Page.Locator("#BankName").FillAsync("Chase Bank");
        await Page.Locator("#AccountName").FillAsync("Acme Corp LLC");
        await Page.Locator("#RoutingNumber").FillAsync("021000021");
        await Page.Locator("#AccountNumber").FillAsync("987654321");
        await Page.Locator("#ReferenceTemplate").FillAsync("Invoice {InvoiceId}");
        await Page.Locator("#EstimatedSettlementTime").FillAsync("1-3 business days");
        await Page.Locator("button[type='submit']").ClickAsync();
        await FindAlertMessageAsync();
    }

    private async Task<IFrame> GetCheckoutFrame()
    {
        await Page.WaitForSelectorAsync("iframe[name='btcpay']");
        var frameElement = await Page.QuerySelectorAsync("iframe[name='btcpay']");
        Assert.NotNull(frameElement);
        var frame = await frameElement.ContentFrameAsync();
        Assert.NotNull(frame);
        await frame.WaitForSelectorAsync("#Checkout");
        return frame;
    }

    [Fact]
    public async Task CanCreateACHPaymentMethod()
    {
        await InitializePlaywright(ServerTester);
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin();
        await GoToUrl("/login");
        await LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
        await CreateACHMethod(user.StoreId);
        await GoToUrl($"/plugins/{user.StoreId}/offline-payments");
        var methodRow = Page.Locator("table tbody tr").Filter(new LocatorFilterOptions { HasText = "ACH" });
        Assert.True(await methodRow.CountAsync() > 0);
    }

    [Fact]
    public async Task CannotCreateDuplicateMethod()
    {
        await InitializePlaywright(ServerTester);
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin();
        await GoToUrl("/login");
        await LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
        await CreateACHMethod(user.StoreId);
        await GoToUrl($"/plugins/{user.StoreId}/offline-payments/create");
        await Page.Locator("#MethodId").SelectOptionAsync("ACH");
        await Page.Locator("#DisplayName").FillAsync("ACH Duplicate");
        await Page.Locator("button[type='submit']").ClickAsync();
        var error = Page.Locator(".text-danger");
        Assert.True(await error.CountAsync() > 0);
    }

    [Fact]
    public async Task ACHAppearsAtCheckout()
    {
        await InitializePlaywright(ServerTester);
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin();
        await ServerTester.ExplorerNode.GenerateAsync(1);
        await user.RegisterDerivationSchemeAsync("BTC", importKeysToNBX: true);
        await GoToUrl("/login");
        await LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
        await CreateACHMethod(user.StoreId);
        var invoiceId = await CreateInvoice(user.StoreId, 10m, "USD");
        await GoToUrl($"tests/index.html?invoice={invoiceId}");
        var frame = await GetCheckoutFrame();
        var achMethod = frame.Locator(".payment-method").Filter(new LocatorFilterOptions { HasText = "ACH" });
        Assert.True(await achMethod.CountAsync() > 0);
    }

    [Fact]
    public async Task CustomerCanMarkPaymentSent()
    {
        await InitializePlaywright(ServerTester);
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin();
        await ServerTester.ExplorerNode.GenerateAsync(1);
        await user.RegisterDerivationSchemeAsync("BTC", importKeysToNBX: true);
        await GoToUrl("/login");
        await LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
        await CreateACHMethod(user.StoreId);
        var invoiceId = await CreateInvoice(user.StoreId, 10m, "USD");
        await GoToUrl($"tests/index.html?invoice={invoiceId}");
        var frame = await GetCheckoutFrame();
        var achMethod = frame.Locator(".payment-method").Filter(new LocatorFilterOptions { HasText = "ACH" });
        if (await achMethod.IsVisibleAsync())
            await achMethod.ClickAsync();

        await Task.Delay(500);
        await frame.Locator("textarea").FillAsync("Transfer sent from Chase");
        await frame.Locator("button.btn-primary").ClickAsync();
        await Task.Delay(1000);
        var confirmation = frame.Locator(".alert-success");
        Assert.True(await confirmation.IsVisibleAsync());
        var invoice = await ServerTester.PayTester.InvoiceRepository.GetInvoice(invoiceId);
        Assert.Equal(InvoiceStatus.New, invoice.Status);
    }

    [Fact]
    public async Task AdminCanSettleInvoiceFromPendingQueue()
    {
        await InitializePlaywright(ServerTester);
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin();
        await ServerTester.ExplorerNode.GenerateAsync(1);
        await user.RegisterDerivationSchemeAsync("BTC", importKeysToNBX: true);
        await GoToUrl("/login");
        await LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
        await CreateACHMethod(user.StoreId);
        var invoiceId = await CreateInvoice(user.StoreId, 10m, "USD");
        await GoToUrl($"tests/index.html?invoice={invoiceId}");
        var frame = await GetCheckoutFrame();
        var achMethod = frame.Locator(".payment-method").Filter(new LocatorFilterOptions { HasText = "ACH" });
        if (await achMethod.IsVisibleAsync())
            await achMethod.ClickAsync();

        await Task.Delay(500);
        await frame.Locator("button.btn-primary").ClickAsync();
        await Task.Delay(1000);
        await GoToUrl($"/plugins/{user.StoreId}/offline-payments/pending");
        var settleButton = Page.Locator("a.text-success").Filter(new LocatorFilterOptions { HasText = "Settle" }).First;
        await settleButton.ClickAsync();
        await Page.Locator("#ConfirmContinue").ClickAsync();
        await FindAlertMessageAsync();
        var invoice = await ServerTester.PayTester.InvoiceRepository.GetInvoice(invoiceId);
        Assert.Equal(InvoiceStatus.Settled, invoice.Status);
    }

    [Fact]
    public async Task AdminCanVoidPayment()
    {
        await InitializePlaywright(ServerTester);
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin();
        await ServerTester.ExplorerNode.GenerateAsync(1);
        await user.RegisterDerivationSchemeAsync("BTC", importKeysToNBX: true);
        await GoToUrl("/login");
        await LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
        await CreateACHMethod(user.StoreId);
        var invoiceId = await CreateInvoice(user.StoreId, 10m, "USD");
        await GoToUrl($"tests/index.html?invoice={invoiceId}");
        var frame = await GetCheckoutFrame();
        var achMethod = frame.Locator(".payment-method").Filter(new LocatorFilterOptions { HasText = "ACH" });
        if (await achMethod.IsVisibleAsync())
            await achMethod.ClickAsync();

        await Task.Delay(500);
        await frame.Locator("button.btn-primary").ClickAsync();
        await Task.Delay(1000);
        await GoToUrl($"/plugins/{user.StoreId}/offline-payments/pending");
        var voidButton = Page.Locator("a.text-danger").Filter(new LocatorFilterOptions { HasText = "Void" }).First;
        await voidButton.ClickAsync();
        await Page.Locator("#ConfirmContinue").ClickAsync();
        await FindAlertMessageAsync();
        var invoice = await ServerTester.PayTester.InvoiceRepository.GetInvoice(invoiceId);
        Assert.Equal(InvoiceStatus.Invalid, invoice.Status);
    }

    [Fact]
    public async Task CannotDeleteMethodWithPendingPayments()
    {
        await InitializePlaywright(ServerTester);
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin();
        await ServerTester.ExplorerNode.GenerateAsync(1);
        await user.RegisterDerivationSchemeAsync("BTC", importKeysToNBX: true);
        await GoToUrl("/login");
        await LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
        await CreateACHMethod(user.StoreId);
        var invoiceId = await CreateInvoice(user.StoreId, 10m, "USD");
        await GoToUrl($"tests/index.html?invoice={invoiceId}");
        var frame = await GetCheckoutFrame();
        var achMethod = frame.Locator(".payment-method").Filter(new LocatorFilterOptions { HasText = "ACH" });
        if (await achMethod.IsVisibleAsync())
            await achMethod.ClickAsync();

        await Task.Delay(500);
        await frame.Locator("button.btn-primary").ClickAsync();
        await Task.Delay(1000);
        await GoToUrl($"/plugins/{user.StoreId}/offline-payments");
        var deleteButton = Page.Locator("a.text-danger").Filter(new LocatorFilterOptions { HasText = "Delete" }).First;
        await deleteButton.ClickAsync();
        await Page.Locator("#ConfirmContinue").ClickAsync();
        await FindAlertMessageAsync(StatusMessageModel.StatusSeverity.Error);
        var methodRow = Page.Locator("table tbody tr").Filter(new LocatorFilterOptions { HasText = "ACH" });
        Assert.True(await methodRow.CountAsync() > 0);
    }
}
