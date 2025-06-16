using System.Text.RegularExpressions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Tests;
using Microsoft.Playwright;
using NBitcoin;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Plugins.Tests;

[Collection("Plugin Tests")]
public class VendorPayPluginUITest : PlaywrightBaseTest
{
    private readonly SharedPluginTestFixture _fixture;

    public VendorPayPluginUITest(SharedPluginTestFixture fixture, ITestOutputHelper helper) : base(helper)
    {
        _fixture = fixture;
        if (_fixture.ServerTester == null) _fixture.Initialize(this);
        ServerTester = _fixture.ServerTester;
    }

    public ServerTester ServerTester { get; }

    public string TestDir { get; private set; }
    public string VendorPayUserName { get; private set; }
    public string VendorPayUserEmail { get; set; }

    [Fact]
    public async Task CreateVendorPayInvoiceWithoutUserTest()
    {
        await InitializePlaywright(ServerTester);
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin(true);
        await GoToUrl("/login");
        await LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
        await GoToUrl($"/plugins/{user.StoreId}/vendorpay/list");
        var expectedSeverity = StatusMessageModel.StatusSeverity.Error;
        await Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { NameString = "Admin Upload Invoice" }).ClickAsync();
        var invoiceCreationStatusText = (await FindAlertMessageAsync(expectedSeverity)).TextContentAsync();
        var actionNotCompleted = expectedSeverity == StatusMessageModel.StatusSeverity.Error &&
                                 (await invoiceCreationStatusText).Trim().ToLower().Contains("to upload a payroll, you need to create a user first");
        Assert.True(actionNotCompleted);
    }

    [Fact]
    public async Task CreateVendorPayInvoiceTest()
    {
        await InitializePlaywright(ServerTester);
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin(true);
        await GoToUrl("/login");
        await LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
        await GoToUrl($"/plugins/{user.StoreId}/vendorpay/users/list");
        await CreateVendorPayUser();
        await GoToUrl($"/plugins/{user.StoreId}/vendorpay/list");
        await MakeInvoiceFileUploadOptional();
        await Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { NameString = "Admin Upload Invoice" }).ClickAsync();
        await CreateVendorPayInvoice("bcrt1qzyzvsqjqn9xzzdgcqhp8c2k9fm5x2napw00v9d");
    }

    [Fact]
    public async Task CreateVendorPayInvoiceTest_InvalidDestinationAddress()
    {
        await InitializePlaywright(ServerTester);
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin(true);
        await GoToUrl("/login");
        await LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
        await GoToUrl($"/plugins/{user.StoreId}/vendorpay/users/list");
        await CreateVendorPayUser();
        await GoToUrl($"/plugins/{user.StoreId}/vendorpay/list");
        await MakeInvoiceFileUploadOptional();
        await Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { NameString = "Admin Upload Invoice" }).ClickAsync();
        await Page.FillAsync("#Destination", "bcrt1qne099wszrhzg4ungad0hnwgjm60euwmzfnxv3h66tf");
        await Page.FillAsync("#Amount", "10");
        await Page.FillAsync("#Description", "Test vendor pay Invoice creation with invalid address");
        await Page.Locator("#Upload").ClickAsync();
        Assert.True(await Page.Locator(".text-danger:has-text('Invalid Destination, check format of address')").IsVisibleAsync());
    }

    [Fact]
    public async Task DeleteVendorPayInvoiceTest()
    {
        var expectedSeverity = StatusMessageModel.StatusSeverity.Success;
        await InitializePlaywright(ServerTester);
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin(true);
        await GoToUrl("/login");
        await LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
        await GoToUrl($"/plugins/{user.StoreId}/vendorpay/users/list");
        await CreateVendorPayUser();
        await GoToUrl($"/plugins/{user.StoreId}/vendorpay/list");
        await MakeInvoiceFileUploadOptional();
        await Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { NameString = "Admin Upload Invoice" }).ClickAsync();
        await CreateVendorPayInvoice("bcrt1qs758ursh4q9z627kt3pp5yysm78ddny6txaqgw");
        var deleteLink = await Page.QuerySelectorAsync("tr:has-text('AwaitingApproval') >> text=Delete");
        Assert.NotNull(deleteLink);
        await deleteLink.ClickAsync();
        await Page.Locator("button[type='submit']").ClickAsync();
        var invoiceDeletionStatusText = (await FindAlertMessageAsync(expectedSeverity)).TextContentAsync();
        var isDeleted = expectedSeverity == StatusMessageModel.StatusSeverity.Success &&
                        (await invoiceDeletionStatusText).Trim() == "Invoice deleted successfully";
        Assert.True(isDeleted);
    }

    [Fact]
    public async Task CreateVendorPayUserTest()
    {
        var expectedSeverity = StatusMessageModel.StatusSeverity.Success;
        await InitializePlaywright(ServerTester);
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin(true);
        await GoToUrl("/login");
        await LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
        await GoToUrl($"/plugins/{user.StoreId}/vendorpay/users/list");
        await CreateVendorPayUser();
        var statusText = (await FindAlertMessageAsync(expectedSeverity)).TextContentAsync();
        var isSuccessful = expectedSeverity == StatusMessageModel.StatusSeverity.Success &&
                           (await statusText).Trim() == "User created successfully";
        Assert.True(isSuccessful);
    }

    [Fact]
    public async Task CreateVendorPayUserTest_PasswordMismatch()
    {
        await InitializePlaywright(ServerTester);
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin(true);
        await GoToUrl("/login");
        await LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
        await GoToUrl($"/plugins/{user.StoreId}/vendorpay/users/list");
        await Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Create User" }).ClickAsync();
        await Page.FillAsync("#Email", user.Email);
        await Page.FillAsync("#Name", "TestUser");
        await Page.FillAsync("#Password", "123456");
        await Page.FillAsync("#ConfirmPassword", "1234567");
        await Page.Locator("#Create").ClickAsync();
        Assert.True(await Page.Locator(".text-danger:has-text('Password fields don\\'t match')").IsVisibleAsync());
    }

    [Fact]
    public async Task DisableAndReEnablePayrollUserTest()
    {
        await InitializePlaywright(ServerTester);
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin(true);
        await GoToUrl("/login");
        await LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
        await GoToUrl($"/plugins/{user.StoreId}/vendorpay/users/list");
        var expectedSeverity = StatusMessageModel.StatusSeverity.Success;
        await CreateVendorPayUser();
        var userRow = Page.GetByText(VendorPayUserName).First.Locator("xpath=ancestor::tr");
        Assert.NotNull(userRow);
        await userRow.GetByRole(AriaRole.Button).Last.ClickAsync();
        await Page.GetByText("Disable").ClickAsync();
        await Page.Locator("button[type='submit']").ClickAsync();
        var disabledStatusText = (await FindAlertMessageAsync(expectedSeverity)).TextContentAsync();
        var isDisabled = expectedSeverity == StatusMessageModel.StatusSeverity.Success &&
                         (await disabledStatusText).Trim() == "User disabled successfully";
        Assert.True(isDisabled);
        await Page.GetByRole(AriaRole.Tab, new PageGetByRoleOptions { NameRegex = new Regex(@"^All\b") }).ClickAsync();
        var allUserRow = Page.GetByText(VendorPayUserName).First.Locator("xpath=ancestor::tr");
        await allUserRow.GetByRole(AriaRole.Button).Last.ClickAsync();
        await Page.GetByText("Activate").ClickAsync();
        await Page.Locator("button[type='submit']").ClickAsync();
        var activateStatusText = (await FindAlertMessageAsync(expectedSeverity)).TextContentAsync();
        var isActivated = expectedSeverity == StatusMessageModel.StatusSeverity.Success &&
                          (await activateStatusText).Trim() == "User activated successfully";
        Assert.True(isActivated);
    }

    [Fact]
    public async Task DeletePayrollUserTest()
    {
        var expectedSeverity = StatusMessageModel.StatusSeverity.Success;
        await InitializePlaywright(ServerTester);
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin(true);
        await GoToUrl("/login");
        await LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
        await GoToUrl($"/plugins/{user.StoreId}/vendorpay/users/list");
        await CreateVendorPayUser();
        var userRow = Page.GetByText(VendorPayUserName).First.Locator("xpath=ancestor::tr");
        Assert.NotNull(userRow);
        await userRow.GetByRole(AriaRole.Button).Last.ClickAsync();
        await Page.Locator("a.dropdown-item.text-danger", new PageLocatorOptions { HasTextString = "Delete" }).ClickAsync();
        await Page.Locator("button[type='submit']").ClickAsync();
        var statusText = (await FindAlertMessageAsync(expectedSeverity)).TextContentAsync();
        var isDeleted = expectedSeverity == StatusMessageModel.StatusSeverity.Success &&
                        (await statusText).Trim() == "User deleted successfully";
        Assert.True(isDeleted);
    }

    [Fact]
    public async Task EditPayrollUserTest()
    {
        await InitializePlaywright(ServerTester);
        var expectedSeverity = StatusMessageModel.StatusSeverity.Success;
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin(true);
        await GoToUrl("/login");
        await LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
        await GoToUrl($"/plugins/{user.StoreId}/vendorpay/users/list");
        await CreateVendorPayUser();
        var userRow = Page.GetByText(VendorPayUserName).First.Locator("xpath=ancestor::tr");
        Assert.NotNull(userRow);
        await userRow.GetByRole(AriaRole.Button).Last.ClickAsync();
        await Page.GetByText("Edit").ClickAsync();
        var newName = "Nosa";
        await Page.FillAsync("#Name", newName);
        await Page.FillAsync("#Email", "testuser@example.com");
        await Page.FillAsync("#reminderInput", "2");
        await Page.Locator("#addReminder").ClickAsync();
        await Page.Locator("#Edit").ClickAsync();
        var statusText = (await FindAlertMessageAsync(expectedSeverity)).TextContentAsync();
        var isEdited = expectedSeverity == StatusMessageModel.StatusSeverity.Success &&
                       (await statusText).Trim() == "User details updated successfully";
        Assert.True(isEdited);
        await Page.WaitForSelectorAsync($"text={newName}");
        var newRecordCount = await Page.Locator("tr", new PageLocatorOptions { HasTextString = newName }).CountAsync();
        Assert.True(newRecordCount > 0, "Updated user row not found.");
    }

    [Fact]
    public async Task ResetVendorPayUserPasswordTest()
    {
        await InitializePlaywright(ServerTester);
        var expectedSeverity = StatusMessageModel.StatusSeverity.Success;
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin(true);
        await GoToUrl("/login");
        await LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
        await GoToUrl($"/plugins/{user.StoreId}/vendorpay/users/list");
        await CreateVendorPayUser();
        var userRow = Page.GetByText(VendorPayUserName).First.Locator("xpath=ancestor::tr");
        Assert.NotNull(userRow);
        await userRow.GetByRole(AriaRole.Button).Last.ClickAsync();
        await Page.GetByText("Edit").ClickAsync();
        await Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { NameRegex = new Regex("reset password", RegexOptions.IgnoreCase) }).ClickAsync();
        await Page.FillAsync("#NewPassword", "123456789");
        await Page.FillAsync("#ConfirmNewPassword", "123456789");
        await Page.Locator("#ResetPassword").ClickAsync();
        var statusText = (await FindAlertMessageAsync(expectedSeverity)).TextContentAsync();
        var isEdited = expectedSeverity == StatusMessageModel.StatusSeverity.Success &&
                       (await statusText).Trim() == "User details updated successfully";
    }

    [Fact]
    public async Task PublicVendorPayUserChangePasswordTest()
    {
        await InitializePlaywright(ServerTester);
        var expectedSeverity = StatusMessageModel.StatusSeverity.Success;
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin(true);
        await GoToUrl("/login");
        await LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
        await GoToUrl($"/plugins/{user.StoreId}/vendorpay/users/list");
        await CreateVendorPayUser();
        await GoToUrl($"/plugins/{user.StoreId}/vendorpay/list");
        await Page.Locator("#StatusOptionsToggle").ClickAsync();
        var popupTask = Page.Context.WaitForPageAsync();
        await Page.Locator("a.dropdown-item", new PageLocatorOptions { HasTextRegex = new Regex("share invoice upload link", RegexOptions.IgnoreCase) })
            .ClickAsync();
        var popup = await popupTask;
        await popup.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        await popup.FillAsync("#Email", VendorPayUserEmail);
        await popup.FillAsync("#Password", "123456");
        await popup.ClickAsync("button[type='submit']");
        await popup.Locator("#StatusOptionsToggle").ClickAsync();
        await popup.Locator("a.dropdown-item", new PageLocatorOptions { HasTextRegex = new Regex("change password", RegexOptions.IgnoreCase) }).ClickAsync();
        await popup.FillAsync("#CurrentPassword", "123456");
        await popup.FillAsync("#NewPassword", "1234567");
        await popup.FillAsync("#ConfirmNewPassword", "1234567");
        await popup.ClickAsync("button[type='submit']");
        var statusText = (await FindAlertMessageAsync(new[] { expectedSeverity }, popup)).TextContentAsync();
        var isEdited = expectedSeverity == StatusMessageModel.StatusSeverity.Success &&
                       (await statusText).Trim() == "Password successfully changed";
        Assert.True(isEdited);
    }

    private async Task CreateVendorPayUser()
    {
        await Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Create User" }).ClickAsync();
        VendorPayUserName = "TestUser";
        VendorPayUserEmail = RandomUtils.GetUInt256().ToString().Substring(64 - 20) + "@a.com";
        await Page.FillAsync("#Email", VendorPayUserEmail);
        await Page.FillAsync("#Name", VendorPayUserName);
        await Page.FillAsync("#Password", "123456");
        await Page.FillAsync("#ConfirmPassword", "123456");
        await Page.Locator("#Create").ClickAsync();
    }

    private async Task CreateVendorPayInvoice(string destWallet)
    {
        var expectedSeverity = StatusMessageModel.StatusSeverity.Success;
        await Page.FillAsync("#Destination", destWallet);
        await Page.FillAsync("#Amount", "10");
        await Page.FillAsync("#Description", "Test Vendor pay Invoice creation");
        await Page.Locator("#Upload").ClickAsync();
        var invoiceCreationStatusText = (await FindAlertMessageAsync(expectedSeverity)).TextContentAsync();
        var isInvoiceCreated = expectedSeverity == StatusMessageModel.StatusSeverity.Success &&
                               (await invoiceCreationStatusText).Trim() == "Invoice uploaded successfully";
        Assert.True(isInvoiceCreated);
    }

    private async Task MakeInvoiceFileUploadOptional()
    {
        await Page.Locator("#StatusOptionsToggle").ClickAsync();
        await Page.Locator("a.dropdown-item", new PageLocatorOptions { HasTextString = "Settings" }).ClickAsync();
        await Page.Locator("#MakeInvoiceFileOptional").CheckAsync();
        await Page.Locator("#Edit").ClickAsync();
        var expectedSeverity = StatusMessageModel.StatusSeverity.Success;
        var statusText = (await FindAlertMessageAsync(expectedSeverity)).TextContentAsync();
        var isSuccessful = expectedSeverity == StatusMessageModel.StatusSeverity.Success &&
                           (await statusText).Trim() == "Vendor pay settings updated successfully";
        Assert.True(isSuccessful);
    }
}
