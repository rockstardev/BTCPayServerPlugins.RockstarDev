using System.Linq;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Services.Invoices;
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
        await user.MakeAdmin(true);
        await GoToUrl($"/login");
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
        await user.MakeAdmin(true);
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
        
        // Select CASH payment method if not already selected
        var cashPaymentMethod = frame.Locator(".payment-method").Filter(new() { HasText = "CASH" });
        if (await cashPaymentMethod.CountAsync() > 0)
        {
            await cashPaymentMethod.ClickAsync();
            await Task.Delay(500); // Wait for component to render
        }
        
        // Click Mark Settled button
        await frame.GetByRole(AriaRole.Link, new() { Name = "Mark Settled" }).ClickAsync();
        
        // Verify redirect happened
        Assert.Equal(new Uri(ServerTester.PayTester.ServerUri, $"tests/index.html?invoice={invoiceId}").ToString(), Page.Url);
        
        // Wait a bit for state transition to complete
        await Task.Delay(1000);
        
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
        await user.MakeAdmin(true);
        await GoToUrl($"/login");
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
