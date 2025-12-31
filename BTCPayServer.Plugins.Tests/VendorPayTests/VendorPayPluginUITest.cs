using System.Globalization;
using System.Text.RegularExpressions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Tests;
using Microsoft.Playwright;
using NBitcoin;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Plugins.Tests;

[Collection("Plugin Tests")]
[Trait("Category", "PlaywrightUITest")]
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
        await user.MakeAdmin();
        await GoToUrl("/login");
        await LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
        await GoToUrl($"/plugins/{user.StoreId}/vendorpay/list");
        var expectedSeverity = StatusMessageModel.StatusSeverity.Error;
        await Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { NameString = "Admin Upload Invoice" }).ClickAsync();
        var invoiceCreationStatusText = (await FindAlertMessageAsync(expectedSeverity)).TextContentAsync();
        var actionNotCompleted = expectedSeverity == StatusMessageModel.StatusSeverity.Error &&
                                 (await invoiceCreationStatusText)?.Trim().ToLower().Contains("to upload a payroll, you need to create a user first") == true;
        Assert.True(actionNotCompleted);
    }

    [Fact]
    public async Task CreateVendorPayInvoiceTest()
    {
        await InitializePlaywright(ServerTester);
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin();
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
        await user.MakeAdmin();
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
        await user.MakeAdmin();
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
        Assert.Equal("Invoice deleted successfully", (await invoiceDeletionStatusText)?.Trim());
    }

    [Fact]
    public async Task CreateVendorPayUserTest()
    {
        var expectedSeverity = StatusMessageModel.StatusSeverity.Success;
        await InitializePlaywright(ServerTester);
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin();
        await GoToUrl("/login");
        await LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
        await GoToUrl($"/plugins/{user.StoreId}/vendorpay/users/list");
        await CreateVendorPayUser();
        var statusText = (await FindAlertMessageAsync(expectedSeverity)).TextContentAsync();
        Assert.Equal("User created successfully", (await statusText)?.Trim());
    }

    [Fact]
    public async Task CreateVendorPayUserTest_PasswordMismatch()
    {
        await InitializePlaywright(ServerTester);
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin();
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
        await user.MakeAdmin();
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
        Assert.Equal("User disabled successfully", (await disabledStatusText)?.Trim());
        await Page.GetByRole(AriaRole.Tab, new PageGetByRoleOptions { NameRegex = new Regex(@"^All\b") }).ClickAsync();
        var allUserRow = Page.GetByText(VendorPayUserName).First.Locator("xpath=ancestor::tr");
        await allUserRow.GetByRole(AriaRole.Button).Last.ClickAsync();
        await Page.GetByText("Activate").ClickAsync();
        await Page.Locator("button[type='submit']").ClickAsync();
        var activateStatusText = (await FindAlertMessageAsync(expectedSeverity)).TextContentAsync();
        Assert.Equal("User activated successfully", (await activateStatusText)?.Trim());
    }

    [Fact]
    public async Task DeletePayrollUserTest()
    {
        var expectedSeverity = StatusMessageModel.StatusSeverity.Success;
        await InitializePlaywright(ServerTester);
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin();
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
        Assert.Equal("User deleted successfully", (await statusText)?.Trim());
    }

    [Fact]
    public async Task EditPayrollUserTest()
    {
        await InitializePlaywright(ServerTester);
        var expectedSeverity = StatusMessageModel.StatusSeverity.Success;
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin();
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
        Assert.Equal("User details updated successfully", (await statusText)?.Trim());
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
        await user.MakeAdmin();
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
        Assert.Equal("User details updated successfully", (await statusText)?.Trim());
    }

    [Fact]
    public async Task PublicVendorPayUserChangePasswordTest()
    {
        await InitializePlaywright(ServerTester);
        var expectedSeverity = StatusMessageModel.StatusSeverity.Success;
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin();
        await GoToUrl("/login");
        await LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
        await GoToUrl($"/plugins/{user.StoreId}/vendorpay/users/list");
        await CreateVendorPayUser();
        await GoToUrl($"/plugins/{user.StoreId}/vendorpay/list");
        var popupTask = Page.Context.WaitForPageAsync();
        await Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { NameRegex = new Regex("Public Invoice Upload", RegexOptions.IgnoreCase) }).ClickAsync();
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
        Assert.Equal("Password successfully changed", (await statusText)?.Trim());
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
        Assert.Equal("Invoice uploaded successfully", (await invoiceCreationStatusText)?.Trim());
    }

    private async Task MakeInvoiceFileUploadOptional()
    {
        var storeId = Page.Url.Split('/')[4];
        await GoToUrl($"/plugins/{storeId}/vendorpay/settings");
        await Page.Locator("#MakeInvoiceFileOptional").CheckAsync();
        await Page.Locator("#Edit").ClickAsync();
        var expectedSeverity = StatusMessageModel.StatusSeverity.Success;
        var statusText = (await FindAlertMessageAsync(expectedSeverity)).TextContentAsync();
        Assert.Equal("Vendor pay settings updated successfully", (await statusText)?.Trim());
    }

    [Fact]
    public async Task VendorPay_PayInvoices_ConversionAdjustment_AppliesOnePercent()
    {
        // Arrange: login and create a vendor user
        await InitializePlaywright(ServerTester);
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin();
        await GoToUrl("/login");
        await LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);

        // Ensure wallet is set up to avoid 404 on wallet send page and to surface the status alert
        await GoToUrl($"/stores/{user.StoreId}/onchain/BTC");
        await AddDerivationScheme();
        // Return to Vendor Pay list to continue the flow

        await GoToUrl($"/plugins/{user.StoreId}/vendorpay/users/list");
        await CreateVendorPayUser();

        // Navigate to list and ensure upload file is optional
        await GoToUrl($"/plugins/{user.StoreId}/vendorpay/list");
        await MakeInvoiceFileUploadOptional();

        // 1) Baseline (no adjustment)
        await Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { NameString = "Admin Upload Invoice" }).ClickAsync();
        await CreateVendorPayInvoice("bcrt1q57aydcw3l7pssaxwz2s3lw4n95qfcnnj6lqyk0");
        var baselineStatus = await PayFirstInvoiceAndGetStatusMessage();
        var baselineDisplayedRate = ExtractFirstDisplayedRate(baselineStatus);

        // 2) Enable +1% adjustment
        await GoToUrl($"/plugins/{user.StoreId}/vendorpay/list");
        await EnableFiatConversionAdjustment(1);

        // Create another invoice and pay again
        var adjustedStatus = await PayFirstInvoiceAndGetStatusMessage();
        var adjustedDisplayedRate = ExtractFirstDisplayedRate(adjustedStatus);

        // Because the display shows USD per BTC (1 / effectiveRate), increasing BTC by 1% reduces the displayed value by ~1%.
        Assert.True(adjustedDisplayedRate < baselineDisplayedRate,
            $"Expected displayed rate to decrease after +1% BTC adjustment. Before: {baselineDisplayedRate}, After: {adjustedDisplayedRate}. Status: '{adjustedStatus}'");

        var relativeDrop = (baselineDisplayedRate - adjustedDisplayedRate) / baselineDisplayedRate;
        // Allow some tolerance due to rounding in the displayed rate (ceil to 2 decimals)
        Assert.True(relativeDrop > 0.005m && relativeDrop < 0.02m,
            $"Expected ~1% decrease; actual change: {relativeDrop * 100m:F2}% (Before: {baselineDisplayedRate}, After: {adjustedDisplayedRate})");

        async Task EnableFiatConversionAdjustment(double percent)
        {
            var storeId = Page.Url.Split('/')[4];
            await GoToUrl($"/plugins/{storeId}/vendorpay/settings");
            await Page.Locator("#InvoiceFiatConversionAdjustment").CheckAsync();
            await Page.FillAsync("#InvoiceFiatConversionAdjustmentPercentage", percent.ToString());
            await Page.Locator("#Edit").ClickAsync();
            var statusText = (await FindAlertMessageAsync()).TextContentAsync();
            Assert.Equal("Vendor pay settings updated successfully", (await statusText)?.Trim());
        }

        async Task<string> PayFirstInvoiceAndGetStatusMessage()
        {
            // Select the first invoice row and click Pay Invoices
            var firstRowCheckbox = Page.Locator("tbody tr.mass-action-row").First.Locator("input.mass-action-select");
            await firstRowCheckbox.CheckAsync();
            await Page.ClickAsync("#payinvoices");
            await Page.WaitForURLAsync(new Regex("/wallets/.+/send", RegexOptions.IgnoreCase), new PageWaitForURLOptions { Timeout = 15000 });

            // The action redirects to the wallet send page where the TempData status is shown
            var alert = await FindAlertMessageAsync(StatusMessageModel.StatusSeverity.Info);
            var text = (await alert.TextContentAsync())?.Trim() ?? string.Empty;
            return text;
        }

        static decimal ExtractFirstDisplayedRate(string statusText)
        {
            // Example: "Vendor Pay on 2025-08-14 for 1 invoices. BTC/USD:100000"
            var m = Regex.Match(statusText ?? string.Empty, @"BTC\/[A-Z]{3}:(?<rate>[0-9]+(?:\.[0-9]+)?)");
            Assert.True(m.Success, $"Could not find displayed rate in status: '{statusText}'");
            return decimal.Parse(m.Groups["rate"].Value, CultureInfo.InvariantCulture);
        }
    }
}
