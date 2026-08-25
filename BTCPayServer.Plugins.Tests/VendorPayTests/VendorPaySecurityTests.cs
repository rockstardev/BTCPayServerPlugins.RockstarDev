using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Tests;
using Microsoft.Playwright;
using NBitcoin;
using Xunit;

namespace BTCPayServer.Plugins.Tests;

// Regression coverage for cross-store invoice access on VendorPayInvoiceController + PublicController.
// Each test creates two independent accounts (each with their own store) and verifies that actions
// scoped to one store's URL do not affect data belonging to a different store.
[Collection("Plugin Tests")]
[Trait("Category", "PlaywrightUITest")]
public class VendorPaySecurityTests : PlaywrightBaseTest
{
    private readonly SharedPluginTestFixture _fixture;

    public VendorPaySecurityTests(SharedPluginTestFixture fixture, ITestOutputHelper helper) : base(helper)
    {
        _fixture = fixture;
        if (_fixture.ServerTester == null) _fixture.Initialize(this);
        ServerTester = _fixture.ServerTester;
    }

    public ServerTester ServerTester { get; }

    private const string PayoutAddress = "bcrt1qzyzvsqjqn9xzzdgcqhp8c2k9fm5x2napw00v9d";

    private async Task<TestAccount> NewAdminAccount()
    {
        var a = ServerTester.NewAccount();
        await a.GrantAccessAsync();
        await a.MakeAdmin();
        return a;
    }

    private async Task ReLogin(TestAccount account)
    {
        Page.SetDefaultTimeout(30000);
        await Page.Context.ClearCookiesAsync();
        await GoToUrl("/login");
        await LogIn(account.RegisterDetails.Email, account.RegisterDetails.Password);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    // Seed a VendorPay setup on the given account and return the invoice id (from the checkbox value).
    private async Task<string> SeedInvoice(TestAccount account)
    {
        await ReLogin(account);
        await GoToUrl($"/plugins/{account.StoreId}/vendorpay/users/list");
        await CreateSeedVendorPayUser();
        await GoToUrl($"/plugins/{account.StoreId}/vendorpay/list");
        await MakeInvoiceFileOptional(account.StoreId);
        await Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { NameString = "Admin Upload Invoice" }).ClickAsync();
        await CreateInvoice(PayoutAddress);
        await GoToUrl($"/plugins/{account.StoreId}/vendorpay/list");
        var id = await Page.Locator("input[name='selectedItems']").First.GetAttributeAsync("value");
        return id;
    }

    private async Task<string> CreateSeedVendorPayUser(string email = null, string name = "TestUser")
    {
        await Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Create User" }).ClickAsync();
        email ??= RandomUtils.GetUInt256().ToString().Substring(64 - 20) + "@a.com";
        await Page.FillAsync("#Email", email);
        await Page.FillAsync("#Name", name);
        await Page.FillAsync("#Password", "123456");
        await Page.FillAsync("#ConfirmPassword", "123456");
        await Page.Locator("#Create").ClickAsync();
        return email;
    }

    // Log in via the public vendor portal (session-based, not BTCPay-account cookie).
    private async Task PublicLogin(string storeId, string email, string password = "123456")
    {
        await Page.Context.ClearCookiesAsync();
        await GoToUrl($"/plugins/{storeId}/vendorpay/public/login");
        await Page.FillAsync("#Email", email);
        await Page.FillAsync("#Password", password);
        await Page.Locator("button[type='submit']").First.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    private async Task MakeInvoiceFileOptional(string storeId)
    {
        await GoToUrl($"/plugins/{storeId}/vendorpay/settings");
        await Page.Locator("#MakeInvoiceFileOptional").CheckAsync();
        await Page.Locator("#Edit").ClickAsync();
        await GoToUrl($"/plugins/{storeId}/vendorpay/list");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    private async Task CreateInvoice(string destination)
    {
        await Page.FillAsync("#Destination", destination);
        await Page.FillAsync("#Amount", "10");
        await Page.FillAsync("#Description", "seed invoice");
        await Page.Locator("#Upload").ClickAsync();
        var msg = (await FindAlertMessageAsync(StatusMessageModel.StatusSeverity.Success)).TextContentAsync();
        Assert.Equal("Invoice uploaded successfully", (await msg)?.Trim());
    }

    private async Task<string> InvoiceStateText(string storeId, string invoiceId)
    {
        await GoToUrl($"/plugins/{storeId}/vendorpay/list");
        var row = Page.Locator($"tr#invoice_{invoiceId}");
        var count = await row.CountAsync();
        if (count == 0) return "(row-missing)";
        // State is in the 6th td (0-indexed 5) - Created / Vendor / Destination / Amount / State.
        return (await row.Locator("td").Nth(5).TextContentAsync())?.Trim() ?? string.Empty;
    }

    // CRITICAL 1a: MassAction command=markpaid must not mutate a different store's invoice.
    [Fact]
    public async Task MassAction_MarkPaid_CrossStore_DoesNotMutate()
    {
        await InitializePlaywright(ServerTester);
        var alice = await NewAdminAccount();
        var bob = await NewAdminAccount();
        var aliceInvoiceId = await SeedInvoice(alice);
        await ReLogin(bob);

        var postUrl = Link($"/plugins/{bob.StoreId}/vendorpay/list");
        var form = Page.APIRequest.CreateFormData()
            .Set("command", "markpaid")
            .Set("selectedItems", aliceInvoiceId);
        await Page.APIRequest.PostAsync(postUrl, new APIRequestContextOptions { Form = form });

        await ReLogin(alice);
        var state = await InvoiceStateText(alice.StoreId, aliceInvoiceId);
        Assert.DoesNotContain("Completed", state);
    }

    // CRITICAL 1b: MassAction command=payinvoices must not funnel a different store's invoice into payment.
    [Fact]
    public async Task MassAction_PayInvoices_CrossStore_DoesNotFunnel()
    {
        await InitializePlaywright(ServerTester);
        var alice = await NewAdminAccount();
        var bob = await NewAdminAccount();
        var aliceInvoiceId = await SeedInvoice(alice);
        await ReLogin(bob);

        var postUrl = Link($"/plugins/{bob.StoreId}/vendorpay/list");
        var form = Page.APIRequest.CreateFormData()
            .Set("command", "payinvoices")
            .Set("selectedItems", aliceInvoiceId);
        await Page.APIRequest.PostAsync(postUrl, new APIRequestContextOptions { Form = form });

        await ReLogin(alice);
        var state = await InvoiceStateText(alice.StoreId, aliceInvoiceId);
        Assert.DoesNotContain("AwaitingPayment", state);
    }

    // CRITICAL 2a: Single-invoice Delete must not remove a different store's invoice.
    [Fact]
    public async Task Delete_CrossStore_DoesNotRemove()
    {
        await InitializePlaywright(ServerTester);
        var alice = await NewAdminAccount();
        var bob = await NewAdminAccount();
        var aliceInvoiceId = await SeedInvoice(alice);
        await ReLogin(bob);

        var postUrl = Link($"/plugins/{bob.StoreId}/vendorpay/delete/{aliceInvoiceId}");
        await Page.APIRequest.PostAsync(postUrl, new APIRequestContextOptions { Form = Page.APIRequest.CreateFormData() });

        await ReLogin(alice);
        var state = await InvoiceStateText(alice.StoreId, aliceInvoiceId);
        Assert.NotEqual("(row-missing)", state);
    }

    // CRITICAL 3: Public vendor portal Delete must not remove another vendor's invoice
    // even when both vendors belong to the same store.
    [Fact]
    public async Task PublicDelete_CrossVendor_DoesNotRemove()
    {
        await InitializePlaywright(ServerTester);
        var admin = await NewAdminAccount();
        await ReLogin(admin);

        // Seed vendor A first + create their invoice (admin upload defaults to A).
        await GoToUrl($"/plugins/{admin.StoreId}/vendorpay/users/list");
        var vendorAEmail = await CreateSeedVendorPayUser(name: "VendorA");
        await GoToUrl($"/plugins/{admin.StoreId}/vendorpay/list");
        await MakeInvoiceFileOptional(admin.StoreId);
        await Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { NameString = "Admin Upload Invoice" }).ClickAsync();
        await CreateInvoice(PayoutAddress);
        await GoToUrl($"/plugins/{admin.StoreId}/vendorpay/list");
        var vendorAInvoiceId = await Page.Locator("input[name='selectedItems']").First.GetAttributeAsync("value");

        // Add vendor B (attacker) in the same store.
        await GoToUrl($"/plugins/{admin.StoreId}/vendorpay/users/list");
        var vendorBEmail = await CreateSeedVendorPayUser(name: "VendorB");

        // Vendor B logs in via public portal + attempts delete on vendor A's invoice.
        await PublicLogin(admin.StoreId, vendorBEmail);
        var postUrl = Link($"/plugins/{admin.StoreId}/vendorpay/public/delete/{vendorAInvoiceId}");
        await Page.APIRequest.PostAsync(postUrl, new APIRequestContextOptions { Form = Page.APIRequest.CreateFormData() });

        // Admin verifies vendor A's invoice still exists.
        await ReLogin(admin);
        var state = await InvoiceStateText(admin.StoreId, vendorAInvoiceId);
        Assert.NotEqual("(row-missing)", state);
    }

    // CRITICAL 2b: Single-invoice AdminNote update must not modify a different store's invoice note.
    [Fact]
    public async Task AdminNote_CrossStore_DoesNotEdit()
    {
        await InitializePlaywright(ServerTester);
        var alice = await NewAdminAccount();
        var bob = await NewAdminAccount();
        var aliceInvoiceId = await SeedInvoice(alice);
        await ReLogin(bob);

        var injected = "TAMPER-" + RandomUtils.GetUInt256().ToString().Substring(0, 8);
        var postUrl = Link($"/plugins/{bob.StoreId}/vendorpay/adminnote/{aliceInvoiceId}");
        var form = Page.APIRequest.CreateFormData()
            .Set("Id", aliceInvoiceId)
            .Set("AdminNote", injected);
        await Page.APIRequest.PostAsync(postUrl, new APIRequestContextOptions { Form = form });

        await ReLogin(alice);
        await GoToUrl($"/plugins/{alice.StoreId}/vendorpay/adminnote/{aliceInvoiceId}");
        var current = await Page.Locator("#AdminNote").InputValueAsync();
        Assert.DoesNotContain(injected, current);
    }
}
