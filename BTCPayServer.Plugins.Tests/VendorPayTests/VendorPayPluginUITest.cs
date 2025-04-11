using System.Text.RegularExpressions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Tests;
using Microsoft.Playwright;
using NBitcoin;
using Xunit;
using Xunit.Abstractions;
using static BTCPayServer.Plugins.Tests.VendorPayTests.VendorPayPluginUITest;

namespace BTCPayServer.Plugins.Tests.VendorPayTests;

public class VendorPayPluginUITest : PlaywrightBaseTest, IClassFixture<VendorPayPluginServerTesterFixture>
{
    public ServerTester ServerTester { get; private set; }
    public string TestDir { get; private set; }
    public string PayrollUserName { get; private set; }

    private readonly VendorPayPluginServerTesterFixture _fixture;

    public VendorPayPluginUITest(VendorPayPluginServerTesterFixture fixture, ITestOutputHelper helper) : base(helper)
    {
        _fixture = fixture;
        if (_fixture.ServerTester == null)
        {
            _fixture.Initialize(this);
        }
        ServerTester = _fixture.ServerTester;
    }

    [Fact]
    public async Task CreateVendorPayInvoiceWithoutUserTest()
    {
        await InitializePlaywright(ServerTester.PayTester.ServerUri);
        await InitializeBTCPayServer();

        var expectedSeverity = StatusMessageModel.StatusSeverity.Error;
        await GoToUrl($"/plugins/{StoreId}/vendorpay/list");
        await Page.GetByRole(AriaRole.Link, new() { NameString = "Admin Upload Invoice" }).ClickAsync();
        var invoiceCreationStatusText = (await FindAlertMessageAsync(expectedSeverity)).TextContentAsync();
        bool isInvoiceCreated = expectedSeverity == StatusMessageModel.StatusSeverity.Error &&
                (await invoiceCreationStatusText).Trim().ToLower().Contains("to upload a payroll, you need to create a user first");
        Assert.True(isInvoiceCreated);
    }

    [Fact]
    public async Task CreateVendorPayInvoiceTest()
    {
        await InitializePlaywright(ServerTester.PayTester.ServerUri);
        await InitializeBTCPayServer();
        var expectedSeverity = StatusMessageModel.StatusSeverity.Success;

        await GoToUrl($"/plugins/{StoreId}/vendorpay/users/list");
        await CreatePayrollUser();

        await GoToUrl($"/plugins/{StoreId}/vendorpay/list");
        await MakeInvoiceFileUploadOptional();

        await Page.GetByRole(AriaRole.Link, new() { NameString = "Admin Upload Invoice" }).ClickAsync();
        await Page.FillAsync("#Destination", "bcrt1qkt36pj0du6cka0nklgjd34mu5m8ffcfanhq9xm");
        await Page.FillAsync("#Amount", "10");
        await Page.FillAsync("#Description", "Test Vendor pay Invoice creation");
        await Page.Locator("#Upload").ClickAsync();
        var invoiceCreationStatusText = (await FindAlertMessageAsync(expectedSeverity)).TextContentAsync();
        bool isInvoiceCreated = expectedSeverity == StatusMessageModel.StatusSeverity.Success &&
                (await invoiceCreationStatusText).Trim() == "Invoice uploaded successfully";
        Assert.True(isInvoiceCreated);
    }

    [Fact]
    public async Task CreateVendorPayInvoiceTest_InvalidDestinationAddress()
    {
        await InitializePlaywright(ServerTester.PayTester.ServerUri);
        await InitializeBTCPayServer();

        await GoToUrl($"/plugins/{StoreId}/vendorpay/users/list");
        await CreatePayrollUser();

        await GoToUrl($"/plugins/{StoreId}/vendorpay/list");
        await MakeInvoiceFileUploadOptional();

        await Page.GetByRole(AriaRole.Link, new() { NameString = "Admin Upload Invoice" }).ClickAsync();
        await Page.FillAsync("#Destination", "bcrt1qkt36pj0du6cka0nklgjd34mu5m");
        await Page.FillAsync("#Amount", "10");
        await Page.FillAsync("#Description", "Test vendor pay Invoice creation with invalid address");
        await Page.Locator("#Upload").ClickAsync();
        Assert.True(await Page.Locator(".text-danger:has-text('Invalid Destination, check format of address')").IsVisibleAsync());
    }

    [Fact]
    public async Task DeleteVendorPayInvoiceTest()
    {
        await InitializePlaywright(ServerTester.PayTester.ServerUri);
        await InitializeBTCPayServer();
        var expectedSeverity = StatusMessageModel.StatusSeverity.Success;

        await GoToUrl($"/plugins/{StoreId}/vendorpay/users/list");
        await CreatePayrollUser();

        await GoToUrl($"/plugins/{StoreId}/vendorpay/list");
        await MakeInvoiceFileUploadOptional();

        await Page.GetByRole(AriaRole.Link, new() { NameString = "Admin Upload Invoice" }).ClickAsync();
        await Page.FillAsync("#Destination", "bcrt1qkt36pj0du6cka0nklgjd34mu5m8ffcfanhq9xm");
        await Page.FillAsync("#Amount", "10");
        await Page.FillAsync("#Description", "Test Vendor pay Invoice creation");
        await Page.Locator("#Upload").ClickAsync();
        var invoiceCreationStatusText = (await FindAlertMessageAsync(expectedSeverity)).TextContentAsync();
        bool isInvoiceCreated = expectedSeverity == StatusMessageModel.StatusSeverity.Success &&
                (await invoiceCreationStatusText).Trim() == "Invoice uploaded successfully";
        Assert.True(isInvoiceCreated);

        var deleteLink = await Page.QuerySelectorAsync("tr:has-text('AwaitingApproval') >> text=Delete");
        Assert.NotNull(deleteLink);
        await deleteLink.ClickAsync();
        await Page.Locator("button[type='submit']").ClickAsync();
        var invoiceDeletionStatusText = (await FindAlertMessageAsync(expectedSeverity)).TextContentAsync();
        bool isDeleted = expectedSeverity == StatusMessageModel.StatusSeverity.Success &&
                (await invoiceDeletionStatusText).Trim() == "Invoice deleted successfully";
        Assert.True(isDeleted);
    }

    [Fact]
    public async Task CreateVendorPayUserTest()
    {
        await InitializePlaywright(ServerTester.PayTester.ServerUri);
        await InitializeBTCPayServer();

        await GoToUrl($"/plugins/{StoreId}/vendorpay/users/list");
        await CreatePayrollUser();
        var expectedSeverity = StatusMessageModel.StatusSeverity.Success;
        var statusText = (await FindAlertMessageAsync(expectedSeverity)).TextContentAsync();
        bool isSuccessful = expectedSeverity == StatusMessageModel.StatusSeverity.Success &&
                (await statusText).Trim() == "User created successfully";
        Assert.True(isSuccessful);
    }

    [Fact]
    public async Task CreateVendorPayUserTest_PasswordMismatch()
    {
        await InitializePlaywright(ServerTester.PayTester.ServerUri);
        await InitializeBTCPayServer();

        await GoToUrl($"/plugins/{StoreId}/vendorpay/users/list");
        await Page.GetByRole(AriaRole.Link, new() { Name = "Create User" }).ClickAsync();

        var user = RandomUtils.GetUInt256().ToString().Substring(64 - 20) + "@a.com";
        await Page.FillAsync("#Email", user);
        await Page.FillAsync("#Name", "TestUser");
        await Page.FillAsync("#Password", "123456");
        await Page.FillAsync("#ConfirmPassword", "1234567");
        await Page.Locator("#Create").ClickAsync();
        Assert.True(await Page.Locator(".text-danger:has-text('Password fields don\\'t match')").IsVisibleAsync());
    }

    [Fact]
    public async Task DisableAndReEnablePayrollUserTest()
    {
        await InitializePlaywright(ServerTester.PayTester.ServerUri);
        await InitializeBTCPayServer();

        await GoToUrl($"/plugins/{StoreId}/vendorpay/users/list");
        var expectedSeverity = StatusMessageModel.StatusSeverity.Success;
        await CreatePayrollUser();
        var userRow = Page.GetByText(PayrollUserName).First.Locator("xpath=ancestor::tr");
        Assert.NotNull(userRow);
        await userRow.GetByRole(AriaRole.Button).Last.ClickAsync();
        await Page.GetByText("Disable").ClickAsync();
        await Page.Locator("button[type='submit']").ClickAsync();
        var disabledStatusText = (await FindAlertMessageAsync(expectedSeverity)).TextContentAsync();
        bool isDisabled = expectedSeverity == StatusMessageModel.StatusSeverity.Success &&
                (await disabledStatusText).Trim() == "User disabled successfully";
        Assert.True(isDisabled);

        await Page.GetByRole(AriaRole.Tab, new() { NameRegex = new Regex(@"^All\b") }).ClickAsync();
        var allUserRow = Page.GetByText(PayrollUserName).First.Locator("xpath=ancestor::tr");
        await allUserRow.GetByRole(AriaRole.Button).Last.ClickAsync();
        await Page.GetByText("Activate").ClickAsync();
        await Page.Locator("button[type='submit']").ClickAsync();
        var activateStatusText = (await FindAlertMessageAsync(expectedSeverity)).TextContentAsync();
        bool isActivated = expectedSeverity == StatusMessageModel.StatusSeverity.Success &&
                (await activateStatusText).Trim() == "User activated successfully";
        Assert.True(isActivated);
    }

    [Fact]
    public async Task DeletePayrollUserTest()
    {
        await InitializePlaywright(ServerTester.PayTester.ServerUri);
        await InitializeBTCPayServer();

        var expectedSeverity = StatusMessageModel.StatusSeverity.Success;
        await GoToUrl($"/plugins/{StoreId}/vendorpay/users/list");
        await CreatePayrollUser();
        var userRow = Page.GetByText(PayrollUserName).First.Locator("xpath=ancestor::tr");
        Assert.NotNull(userRow);
        await userRow.GetByRole(AriaRole.Button).Last.ClickAsync();
        await Page.Locator("a.dropdown-item.text-danger", new() { HasTextString = "Delete" }).ClickAsync();
        await Page.Locator("button[type='submit']").ClickAsync();
        var statusText = (await FindAlertMessageAsync(expectedSeverity)).TextContentAsync();
        bool isDeleted = expectedSeverity == StatusMessageModel.StatusSeverity.Success &&
                (await statusText).Trim() == "User deleted successfully";
        Assert.True(isDeleted);
    }

    // Add test to try to delete a user with invoices


    [Fact]
    public async Task EditPayrollUserTest()
    {
        await InitializePlaywright(ServerTester.PayTester.ServerUri);
        await InitializeBTCPayServer();
        var expectedSeverity = StatusMessageModel.StatusSeverity.Success;
        await GoToUrl($"/plugins/{StoreId}/vendorpay/users/list");
        await CreatePayrollUser();
        var userRow = Page.GetByText(PayrollUserName).First.Locator("xpath=ancestor::tr");
        Assert.NotNull(userRow);
        await userRow.GetByRole(AriaRole.Button).Last.ClickAsync();
        await Page.GetByText("Edit").ClickAsync();

        string newName = "Nosa";
        await Page.FillAsync("#Name", newName);
        await Page.FillAsync("#Email", "testuser@example.com");
        await Page.FillAsync("#reminderInput", "2");
        await Page.Locator("#addReminder").ClickAsync();
        await Page.Locator("#Edit").ClickAsync();

        var statusText = (await FindAlertMessageAsync(expectedSeverity)).TextContentAsync();
        bool isEdited = expectedSeverity == StatusMessageModel.StatusSeverity.Success &&
                (await statusText).Trim() == "User details updated successfully";
        Assert.True(isEdited);
        await Page.WaitForSelectorAsync($"text={newName}");
        var newRecordCount = await Page.Locator("tr", new() { HasTextString = newName }).CountAsync();
        Assert.True(newRecordCount > 0, "Updated user row not found.");
    }

    [Fact]
    public async Task ResetPayrollUserPasswordTest()
    {
        await InitializePlaywright(ServerTester.PayTester.ServerUri);
        await InitializeBTCPayServer();
        var expectedSeverity = StatusMessageModel.StatusSeverity.Success;
        await GoToUrl($"/plugins/{StoreId}/vendorpay/users/list");
        await CreatePayrollUser();
        var userRow = Page.GetByText(PayrollUserName).First.Locator("xpath=ancestor::tr");
        Assert.NotNull(userRow);
        await userRow.GetByRole(AriaRole.Button).Last.ClickAsync();
        await Page.GetByText("Edit").ClickAsync();
        await Page.GetByRole(AriaRole.Link, new() { NameRegex = new Regex("reset password", RegexOptions.IgnoreCase) }).ClickAsync();
        await Page.FillAsync("#NewPassword", "123456789");
        await Page.FillAsync("#ConfirmNewPassword", "123456789");
        await Page.Locator("#ResetPassword").ClickAsync();
        var statusText = (await FindAlertMessageAsync(expectedSeverity)).TextContentAsync();
        bool isEdited = expectedSeverity == StatusMessageModel.StatusSeverity.Success &&
                (await statusText).Trim() == "User details updated successfully";
        Assert.True(isEdited);
    }

    private async Task CreatePayrollUser()
    {
        await Page.GetByRole(AriaRole.Link, new() { Name = "Create User" }).ClickAsync();
        PayrollUserName = "TestUser";
        var email = RandomUtils.GetUInt256().ToString().Substring(64 - 20) + "@a.com";
        await Page.FillAsync("#Email", email);
        await Page.FillAsync("#Name", PayrollUserName);
        await Page.FillAsync("#Password", "123456");
        await Page.FillAsync("#ConfirmPassword", "123456");
        await Page.Locator("#Create").ClickAsync();
    }

    private async Task MakeInvoiceFileUploadOptional()
    {
        await Page.Locator("#StatusOptionsToggle").ClickAsync();
        await Page.Locator("a.dropdown-item", new PageLocatorOptions { HasTextString = "Settings" }).ClickAsync();
        await Page.Locator("#MakeInvoiceFileOptional").CheckAsync();
        await Page.Locator("#Edit").ClickAsync();
        var expectedSeverity = StatusMessageModel.StatusSeverity.Success;
        var statusText = (await FindAlertMessageAsync(expectedSeverity)).TextContentAsync();
        bool isSuccessful = expectedSeverity == StatusMessageModel.StatusSeverity.Success &&
                (await statusText).Trim() == "Vendor pay settings updated successfully";
        Assert.True(isSuccessful);
    }

    public class VendorPayPluginServerTesterFixture : IDisposable
    {
        public ServerTester ServerTester { get; private set; }

        public void Initialize(PlaywrightBaseTest testInstance)
        {
            if (ServerTester == null)
            {
                var testDir = Path.Combine(Directory.GetCurrentDirectory(), "VendorPayPluginUITest");
                ServerTester = testInstance.CreateServerTester(testDir, newDb: true);
                ServerTester.StartAsync().GetAwaiter().GetResult();
            }
        }
        public void Dispose()
        {
            ServerTester?.Dispose();
            ServerTester = null;
        }
    }
}
