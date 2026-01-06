using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Tests;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Plugins.Tests;

[Collection("Plugin Tests")]
[Trait("Category", "PlaywrightUITest")]
public class MarkPaidPluginUITest : PlaywrightBaseTest
{
    private readonly SharedPluginTestFixture _fixture;

    public MarkPaidPluginUITest(SharedPluginTestFixture fixture, ITestOutputHelper helper) : base(helper)
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
        await InitializePlaywright(ServerTester);
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin();
        await GoToUrl("/login");
        await LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
        await GoToUrl($"/stores/{user.StoreId}/markpaid/method/CASH");
        var checkboxSelector = "input#Enabled";
        var checkBox = await Page.QuerySelectorAsync(checkboxSelector);
        Assert.NotNull(checkBox);
        var isChecked = await checkBox.IsCheckedAsync();
        if (!isChecked)
            await checkBox.CheckAsync();

        await Page.Locator("button[type='submit']").ClickAsync();
        checkBox = await Page.QuerySelectorAsync(checkboxSelector);
        Assert.NotNull(checkBox);
        Assert.True(await checkBox.IsCheckedAsync());
    }

    [Fact]
    public async Task CanUseCheckoutAsModal()
    {
        await InitializePlaywright(ServerTester);
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin();
        await GoToUrl("/login");
        await LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
        await GoToUrl($"/stores/{user.StoreId}/markpaid/method/CASH");
        var checkboxSelector = "input#Enabled";
        var checkBox = await Page.QuerySelectorAsync(checkboxSelector);
        Assert.NotNull(checkBox);
        var isChecked = await checkBox.IsCheckedAsync();
        if (!isChecked)
            await checkBox.CheckAsync();

        await Page.Locator("button[type='submit']").ClickAsync();
        checkBox = await Page.QuerySelectorAsync(checkboxSelector);
        Assert.NotNull(checkBox);
        Assert.True(await checkBox.IsCheckedAsync());

        // Create invoice and verify initial state
        var invoiceId = await CreateInvoice(user.StoreId, 0.001m, "BTC", "a@x.com");
        var invoice = await ServerTester.PayTester.InvoiceRepository.GetInvoice(invoiceId);
        Assert.Equal(InvoiceStatus.New, invoice.Status);
        var initialPayments = invoice.GetPayments(false);
        Assert.Empty(initialPayments);

        // Navigate to checkout
        await GoToUrl($"tests/index.html?invoice={invoiceId}");
        await Page.WaitForSelectorAsync("iframe[name='btcpay']");
        var frameElement = await Page.QuerySelectorAsync("iframe[name='btcpay']");
        Assert.NotNull(frameElement);
        var frame = await frameElement.ContentFrameAsync();
        Assert.NotNull(frame);
        await frame.WaitForSelectorAsync("#Checkout");

        // Select CASH payment method if selector is visible
        // Note: BTCPayServer hides the payment method selector when there's only one method
        var cashPaymentMethod = frame.Locator(".payment-method").Filter(new LocatorFilterOptions { HasText = "CASH" });
        if (await cashPaymentMethod.CountAsync() > 0 && await cashPaymentMethod.IsVisibleAsync())
        {
            await cashPaymentMethod.ClickAsync();
            await Task.Delay(500); // Wait for component to render
        }

        // Click Mark Settled button and wait for page reload
        await frame.GetByRole(AriaRole.Link, new FrameGetByRoleOptions { Name = "Mark Settled" }).ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Re-acquire the checkout frame after reload
        frameElement = await Page.QuerySelectorAsync("iframe[name='btcpay']");
        Assert.NotNull(frameElement);
        frame = await frameElement.ContentFrameAsync();
        Assert.NotNull(frame);
        await frame.WaitForSelectorAsync("#settled h4");
        await frame.WaitForTimeoutAsync(500); // Wait for i18n to populate and script to append method

        // Verify settled heading includes payment method on first render
        var settledHeading = await frame.InnerTextAsync("#settled h4");
        Assert.Contains(" - CASH", settledHeading);

        // Switch language and ensure heading remains annotated
        await frame.SelectOptionAsync("#DefaultLang", "it-IT");
        await frame.WaitForTimeoutAsync(100); // Wait for i18n to update and script to re-append
        settledHeading = await frame.InnerTextAsync("#settled h4");
        Assert.Contains(" - CASH", settledHeading);

        // Verify invoice is now settled
        invoice = await ServerTester.PayTester.InvoiceRepository.GetInvoice(invoiceId);
        Assert.Equal(InvoiceStatus.Settled, invoice.Status);

        // Verify payment was added with correct details
        var payments = invoice.GetPayments(false);
        Assert.Single(payments);
        var payment = payments.First();
        Assert.Equal(PaymentStatus.Settled, payment.Status);
        Assert.Equal("CASH", payment.PaymentMethodId.ToString());
        Assert.Equal(invoice.Price, payment.InvoicePaidAmount.Net);
    }

    [Fact]
    public async Task DisableCashPaymentTest()
    {
        await InitializePlaywright(ServerTester);
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin();
        await GoToUrl("/login");
        await LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
        await GoToUrl($"/stores/{user.StoreId}/markpaid/method/CASH");
        var checkboxSelector = "input#Enabled";
        var checkBox = await Page.QuerySelectorAsync(checkboxSelector);
        Assert.NotNull(checkBox);
        var isChecked = await checkBox.IsCheckedAsync();
        if (isChecked)
            await checkBox.UncheckAsync();

        await Page.Locator("button[type='submit']").ClickAsync();
        checkBox = await Page.QuerySelectorAsync(checkboxSelector);
        Assert.NotNull(checkBox);
        Assert.False(await checkBox.IsCheckedAsync());
    }
}
