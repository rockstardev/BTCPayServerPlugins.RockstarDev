using System;
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
    public async Task VendorPaySettings_DisallowBlankInviteTemplate()
    {
        await InitializePlaywright(ServerTester);
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin();
        await GoToUrl("/login");
        await LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);

        await GoToUrl($"/plugins/{user.StoreId}/vendorpay/settings");
        await Page.FillAsync("#UserInviteEmailSubject", string.Empty);
        await Page.FillAsync("#UserInviteEmailBody", string.Empty);
        await Page.Locator("#Edit").ClickAsync();

        Assert.True(await Page.Locator(".text-danger:has-text('Invite email subject cannot be empty')").IsVisibleAsync());
        Assert.True(await Page.Locator(".text-danger:has-text('Invite email template cannot be empty')").IsVisibleAsync());
    }

    [Fact]
    public async Task EmailConfirmation_UsesRegtestMempoolAddressPlaceholder()
    {
        await InitializePlaywright(ServerTester);
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin();
        await GoToUrl("/login");
        await LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);

        var storeId = user.StoreId;
        await GoToUrl($"/stores/{storeId}/email-settings");
        await Page.ClickAsync("#mailpit");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await GoToUrl($"/plugins/{storeId}/vendorpay/settings");
        await Page.Locator("#MakeInvoiceFileOptional").CheckAsync();
        await Page.Locator("#emailToggle").CheckAsync();
        await Page.FillAsync("#EmailOnInvoicePaidSubject", "[VendorPay] Invoice paid");
        await Page.FillAsync("#EmailOnInvoicePaidBody", "Address tracker: {MempoolAddress}");
        await Page.Locator("#Edit").ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        await Page.ScreenshotAsync(new PageScreenshotOptions { Path = "settings-save-result.png" });
        await FindAlertMessageAsync(StatusMessageModel.StatusSeverity.Success);

        await GoToUrl($"/plugins/{storeId}/vendorpay/users/list");
        await CreateVendorPayUser();
        const string destination = "bcrt1qaeqay34jh9y3j4q5qkavuj2evj439hj7nprlvs";
        await GoToUrl($"/plugins/{storeId}/vendorpay/list");
        await Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { NameString = "Admin Upload Invoice" }).ClickAsync();
        await CreateVendorPayInvoice(destination);

        var firstRowCheckbox = Page.Locator("tbody tr.mass-action-row").First.Locator("input.mass-action-select");
        await firstRowCheckbox.CheckAsync();
        await Page.ClickAsync("#markpaid");
        await FindAlertMessageAsync(StatusMessageModel.StatusSeverity.Success);

        await firstRowCheckbox.CheckAsync();
        var emailMessage = await ServerTester.AssertHasEmail(async () =>
        {
            await Page.ClickAsync("#emailconfirmation");
            await FindAlertMessageAsync(StatusMessageModel.StatusSeverity.Success);
        });

        Assert.Contains("https://mempool.space/regtest/address/", emailMessage.Text);
        Assert.Contains(destination, emailMessage.Text);
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
        await Page.Locator(".day-btn[data-day='2']").ClickAsync();
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

    [Fact]
    public async Task AccountlessInvoiceUpload_HappyPath()
    {
        await InitializePlaywright(ServerTester);
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin();

        await GoToUrl("/login");
        await LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);

        var storeId = user.StoreId;
        var uploadCode = $"CODE{Guid.NewGuid():N}"[..10];
        const string descriptionQuestion = "How many people were at the meetup?";

        await ConfigureAccountlessUploadSettings(storeId, uploadCode, descriptionQuestion);

        // Reload settings to verify persistence and capture the accountless link
        await GoToUrl($"/plugins/{storeId}/vendorpay/settings");
        Assert.True(await Page.Locator("#MakeInvoiceFileOptional").IsCheckedAsync());
        Assert.True(await Page.Locator("#accountlessUploadToggle").IsCheckedAsync());
        Assert.Equal(descriptionQuestion, await Page.InputValueAsync("#DescriptionTitle"));

        await Page.WaitForSelectorAsync("#accountlessUploadLink");
        var accountlessLink = await Page.InputValueAsync("#accountlessUploadLink");
        Assert.False(string.IsNullOrWhiteSpace(accountlessLink));

        // Open accountless upload page and complete the form
        var accountlessPage = await Page.Context.NewPageAsync();
        await accountlessPage.GotoAsync(accountlessLink);
        await accountlessPage.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        await accountlessPage.WaitForSelectorAsync($"text={descriptionQuestion}");

        var accountlessEmail = $"meetup-{RandomUtils.GetUInt256().ToString()[^10..]}@example.com";
        const string accountlessName = "Accountless Tester";
        const string destinationAddress = "bcrt1qaeqay34jh9y3j4q5qkavuj2evj439hj7nprlvs";
        /*  more addresses for future if needed
        bcrt1q8eehdf2ye6jzyp2j5kaj04swu87f45e8lqv8yy
        bcrt1q6q7z54w9u8nxz2k322hzsfknn4u8g90khu5rct
        bcrt1qvdj727gddc4s2zdppz6f2d6wml4hkww6alj07v
        bcrt1q02yz4j0ttyjxjfx6yz4mlw40lat6c0xe9k5824
         */

        await accountlessPage.FillAsync("#UploadCode", uploadCode);
        await accountlessPage.FillAsync("#Name", accountlessName);
        await accountlessPage.FillAsync("#Email", accountlessEmail);
        await accountlessPage.FillAsync("#Destination", destinationAddress);
        await accountlessPage.FillAsync("#Amount", "25");
        await accountlessPage.FillAsync("#Currency", "USD");
        await accountlessPage.FillAsync("#Description", "About 42 attendees");

        // Wait for form to be fully loaded and submit button to be ready
        var submitButton = accountlessPage.Locator("button[type='submit']");
        await submitButton.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        // Click submit and wait for navigation
        await submitButton.ClickAsync();

        // Check for validation errors
        var validationErrors = accountlessPage.Locator(".text-danger:visible");
        var errorCount = await validationErrors.CountAsync();
        if (errorCount > 0)
        {
            var errorText = await validationErrors.First.TextContentAsync();
            Assert.Fail($"Form validation failed: {errorText}");
        }

        await accountlessPage.WaitForSelectorAsync("h2:has-text('Invoice Uploaded Successfully!')", new PageWaitForSelectorOptions { Timeout = 60000 });
        await accountlessPage.CloseAsync();

        // Verify invoice appears in admin list view (Active tab by default)
        await GoToUrl($"/plugins/{storeId}/vendorpay/list");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Wait for the table to load
        await Page.WaitForSelectorAsync("table.mass-action tbody", new PageWaitForSelectorOptions { Timeout = 10000 });

        await Page.ScreenshotAsync(new PageScreenshotOptions { Path = $"invoice-list-{storeId}.png" });

        // Check all rows in the table
        var allRows = Page.Locator("table.mass-action tbody tr.mass-action-row");
        var totalRows = await allRows.CountAsync();

        // Look for the invoice by the visible user name
        var newInvoiceRow = Page.Locator("table.mass-action tbody tr.mass-action-row", new PageLocatorOptions { HasTextString = accountlessName });
        var rowCount = await newInvoiceRow.CountAsync();

        Assert.True(rowCount > 0, $"Accountless invoice not found. Name: {accountlessName}, Total rows: {totalRows}, Matching rows: {rowCount}");
        Assert.True(await newInvoiceRow.First.Locator("text=AwaitingApproval").IsVisibleAsync());

        // Configure email settings to use MailPit
        await GoToUrl($"/stores/{storeId}/email-settings");
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        
        // Click "Use mailpit" button to auto-configure email settings
        await Page.ClickAsync("#mailpit");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Enable email uploader confirmation in VendorPay settings
        await GoToUrl($"/plugins/{storeId}/vendorpay/settings");
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        
        // Ensure accountless upload is still enabled and expand the section
        if (!await Page.Locator("#accountlessUploadToggle").IsCheckedAsync())
        {
            await Page.Locator("#accountlessUploadToggle").ClickAsync();
        }
        
        // Enable email uploader confirmation toggle
        await Page.Locator("#emailUploaderUploadToggle").SetCheckedAsync(true);
        
        // Fill in email subject and body
        await Page.FillAsync("#EmailUploaderOnInvoiceUploadedSubject", "[VendorPay] Invoice Upload Confirmation");
        await Page.FillAsync("#EmailUploaderOnInvoiceUploadedBody", "Hello {VendorName},\n\nThank you for uploading your invoice.\n\nInvoice ID: {InvoiceId}\nAmount: {Amount} {Currency}\n\nThank you,\n{StoreName}");
        
        // Save settings
        await Page.ClickAsync("button[type='submit']");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Upload second invoice and verify email is sent
        var secondEmail = $"meetup2-{RandomUtils.GetUInt256().ToString()[^10..]}@example.com";
        const string secondName = "Second Uploader";
        const string secondDestination = "bcrt1q8eehdf2ye6jzyp2j5kaj04swu87f45e8lqv8yy";
        
        var secondUploadPage = await Page.Context.NewPageAsync();
        await secondUploadPage.GotoAsync(accountlessLink);
        await secondUploadPage.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        
        await secondUploadPage.FillAsync("#UploadCode", uploadCode);
        await secondUploadPage.FillAsync("#Name", secondName);
        await secondUploadPage.FillAsync("#Email", secondEmail);
        await secondUploadPage.FillAsync("#Destination", secondDestination);
        await secondUploadPage.FillAsync("#Amount", "50");
        await secondUploadPage.FillAsync("#Currency", "USD");
        await secondUploadPage.FillAsync("#Description", "About 100 attendees");
        
        // Submit and wait for success
        var secondSubmitButton = secondUploadPage.Locator("button[type='submit']");
        await secondSubmitButton.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        
        var emailMessage = await ServerTester.AssertHasEmail(async () =>
        {
            await secondSubmitButton.ClickAsync();
            await secondUploadPage.WaitForSelectorAsync("h2:has-text('Invoice Uploaded Successfully!')", new PageWaitForSelectorOptions { Timeout = 60000 });
        });
        
        await secondUploadPage.CloseAsync();
        
        // Verify email was sent correctly
        Assert.Equal("[VendorPay] Invoice Upload Confirmation", emailMessage.Subject);
        Assert.Contains("Thank you for uploading your invoice", emailMessage.Text);
        Assert.Equal(secondEmail, emailMessage.To[0].Address);
        Assert.Contains(secondName, emailMessage.Text);
        
        // Verify second invoice appears in admin list
        await GoToUrl($"/plugins/{storeId}/vendorpay/list");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.WaitForSelectorAsync("table.mass-action tbody", new PageWaitForSelectorOptions { Timeout = 10000 });
        
        var secondInvoiceRow = Page.Locator("table.mass-action tbody tr.mass-action-row", new PageLocatorOptions { HasTextString = secondName });
        var secondRowCount = await secondInvoiceRow.CountAsync();
        
        Assert.True(secondRowCount > 0, $"Second accountless invoice not found. Name: {secondName}");
        Assert.True(await secondInvoiceRow.First.Locator("text=AwaitingApproval").IsVisibleAsync());
    }

    [Fact]
    public async Task AccountlessInvoiceUpload_SecurityScenarios()
    {
        await InitializePlaywright(ServerTester);
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin();

        await GoToUrl("/login");
        await LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);

        var storeId = user.StoreId;
        var uploadCode = $"CODE{Guid.NewGuid():N}"[..10];
        const string descriptionQuestion = "How many people were at the meetup?";

        await ConfigureAccountlessUploadSettings(storeId, uploadCode, descriptionQuestion);

        await GoToUrl($"/plugins/{storeId}/vendorpay/settings");
        await Page.WaitForSelectorAsync("#accountlessUploadLink");
        var accountlessLink = await Page.InputValueAsync("#accountlessUploadLink");
        Assert.False(string.IsNullOrWhiteSpace(accountlessLink));

        // Test 1: Email Collision - Active User
        var activeUserEmail = $"active-{RandomUtils.GetUInt256().ToString()[^10..]}@test.com";
        await GoToUrl($"/plugins/{storeId}/vendorpay/users/list");
        await Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Create User" }).ClickAsync();
        await Page.FillAsync("#Email", activeUserEmail);
        await Page.FillAsync("#Name", "Active User");
        await Page.FillAsync("#Password", "123456");
        await Page.FillAsync("#ConfirmPassword", "123456");
        await Page.Locator("#Create").ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var activePage = await Page.Context.NewPageAsync();
        await AttemptAccountlessUpload(activePage, accountlessLink, uploadCode, activeUserEmail, "Collision Test", "bcrt1qaeqay34jh9y3j4q5qkavuj2evj439hj7nprlvs", descriptionQuestion);
        await AssertValidationError(activePage, "This email is registered. Please log in to upload invoices.");

        // Test 2: Email Collision - Pending User (Pending is default state for new users)
        var pendingUserEmail = $"pending-{RandomUtils.GetUInt256().ToString()[^10..]}@test.com";
        await GoToUrl($"/plugins/{storeId}/vendorpay/users/list");
        await Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Create User" }).ClickAsync();
        await Page.FillAsync("#Email", pendingUserEmail);
        await Page.FillAsync("#Name", "Pending User");
        await Page.FillAsync("#Password", "123456");
        await Page.FillAsync("#ConfirmPassword", "123456");
        await Page.Locator("#Create").ClickAsync();

        var pendingPage = await Page.Context.NewPageAsync();
        await AttemptAccountlessUpload(pendingPage, accountlessLink, uploadCode, pendingUserEmail, "Collision Test", "bcrt1q8eehdf2ye6jzyp2j5kaj04swu87f45e8lqv8yy", descriptionQuestion);
        await AssertValidationError(pendingPage, "This email is registered. Please log in to upload invoices.");

        // Test 3: Email Reuse - Accountless User (can upload multiple times)
        var accountlessEmail = $"accountless-{RandomUtils.GetUInt256().ToString()[^10..]}@test.com";
        var firstUploadPage = await Page.Context.NewPageAsync();
        await AttemptAccountlessUpload(firstUploadPage, accountlessLink, uploadCode, accountlessEmail, "John Doe", "bcrt1q6q7z54w9u8nxz2k322hzsfknn4u8g90khu5rct", descriptionQuestion);
        await AssertUploadSuccess(firstUploadPage);

        // Second upload with same email should also succeed (accountless users can upload multiple times)
        var secondUploadPage = await Page.Context.NewPageAsync();
        await AttemptAccountlessUpload(secondUploadPage, accountlessLink, uploadCode, accountlessEmail, "Jane Smith", "bcrt1qvdj727gddc4s2zdppz6f2d6wml4hkww6alj07v", descriptionQuestion);
        await AssertUploadSuccess(secondUploadPage);

        // Verify both invoices were created
        await GoToUrl($"/plugins/{storeId}/vendorpay/list");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.WaitForSelectorAsync("table.mass-action tbody");

        var johnDoeRow = Page.Locator("table.mass-action tbody tr.mass-action-row", new PageLocatorOptions { HasTextString = "John Doe" });
        Assert.True(await johnDoeRow.CountAsync() > 0, "First invoice with John Doe should exist");

        // Test 4: Email Collision - Disabled User (same as Active/Pending, cannot upload)
        var disabledUserEmail = $"disabled-{RandomUtils.GetUInt256().ToString()[^10..]}@test.com";
        await GoToUrl($"/plugins/{storeId}/vendorpay/users/list");
        await Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Create User" }).ClickAsync();
        await Page.FillAsync("#Email", disabledUserEmail);
        await Page.FillAsync("#Name", "Disabled User");
        await Page.FillAsync("#Password", "123456");
        await Page.FillAsync("#ConfirmPassword", "123456");
        await Page.Locator("#Create").ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Find the user row and click the dropdown toggle button
        var disabledUserRow = Page.Locator("tbody tr", new PageLocatorOptions { HasTextString = disabledUserEmail }).First;
        var toggleButton = disabledUserRow.Locator("button[id^='ToggleActions-']");
        await toggleButton.ClickAsync();

        // Click the Disable link in the dropdown
        var disableLink = disabledUserRow.Locator("a.dropdown-item", new LocatorLocatorOptions { HasTextString = "Disable" });
        await disableLink.ClickAsync();
        await Page.Locator("button[type='submit']").ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Disabled users are still registered users, so they should be rejected
        var disabledUserPage = await Page.Context.NewPageAsync();
        await AttemptAccountlessUpload(disabledUserPage, accountlessLink, uploadCode, disabledUserEmail, "Disabled User Test", "bcrt1q02yz4j0ttyjxjfx6yz4mlw40lat6c0xe9k5824", descriptionQuestion);
        await AssertValidationError(disabledUserPage, "This email is registered. Please log in to upload invoices.");

        // Test 6: Invalid Upload Code
        var invalidCodeEmail = $"invalid-{RandomUtils.GetUInt256().ToString()[^10..]}@test.com";
        var invalidCodePage = await Page.Context.NewPageAsync();
        await AttemptAccountlessUpload(invalidCodePage, accountlessLink, "WRONGCODE", invalidCodeEmail, "Invalid Code Test", "bcrt1qaeqay34jh9y3j4q5qkavuj2evj439hj7nprlvs", descriptionQuestion);
        await AssertValidationError(invalidCodePage, "Invalid upload code");

        // Test 7: Missing Security Answer (Description)
        var missingAnswerEmail = $"missing-{RandomUtils.GetUInt256().ToString()[^10..]}@test.com";
        var missingAnswerPage = await Page.Context.NewPageAsync();
        await missingAnswerPage.GotoAsync(accountlessLink);
        await missingAnswerPage.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        await missingAnswerPage.FillAsync("#UploadCode", uploadCode);
        await missingAnswerPage.FillAsync("#Name", "Missing Answer Test");
        await missingAnswerPage.FillAsync("#Email", missingAnswerEmail);
        await missingAnswerPage.FillAsync("#Destination", "bcrt1q8eehdf2ye6jzyp2j5kaj04swu87f45e8lqv8yy");
        await missingAnswerPage.FillAsync("#Amount", "25");
        await missingAnswerPage.FillAsync("#Currency", "USD");

        var submitButton = missingAnswerPage.Locator("button[type='submit']");
        await submitButton.ClickAsync();
        await Task.Delay(1000);

        await AssertValidationError(missingAnswerPage, "Description is required");

        // Test 8: Feature Disabled (404)
        await GoToUrl($"/plugins/{storeId}/vendorpay/settings");
        var accountlessToggle = Page.Locator("#accountlessUploadToggle");
        if (await accountlessToggle.IsCheckedAsync())
            await accountlessToggle.UncheckAsync();
        await Page.Locator("#Edit").ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var disabledPage = await Page.Context.NewPageAsync();
        var response = await disabledPage.GotoAsync(accountlessLink);
        Assert.Equal(404, response.Status);
        await disabledPage.CloseAsync();
    }

    private async Task AttemptAccountlessUpload(IPage uploadPage, string accountlessLink, string uploadCode, string email, string name, string destination, string descriptionQuestion)
    {
        await uploadPage.GotoAsync(accountlessLink);
        await uploadPage.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        await uploadPage.WaitForSelectorAsync($"text={descriptionQuestion}");

        await uploadPage.FillAsync("#UploadCode", uploadCode);
        await uploadPage.FillAsync("#Name", name);
        await uploadPage.FillAsync("#Email", email);
        await uploadPage.FillAsync("#Destination", destination);
        await uploadPage.FillAsync("#Amount", "25");
        await uploadPage.FillAsync("#Currency", "USD");
        await uploadPage.FillAsync("#Description", "Test description");

        var submitButton = uploadPage.Locator("button[type='submit']");
        await submitButton.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await submitButton.ClickAsync();
        await Task.Delay(1000);
    }

    private async Task AssertValidationError(IPage uploadPage, string expectedError)
    {
        var validationErrors = uploadPage.Locator(".text-danger:visible");
        var errorCount = await validationErrors.CountAsync();
        Assert.True(errorCount > 0, $"Expected validation error but none found. Expected: {expectedError}");

        var errorText = await validationErrors.First.TextContentAsync();
        Assert.Contains(expectedError, errorText, StringComparison.OrdinalIgnoreCase);

        await uploadPage.CloseAsync();
    }

    private async Task AssertUploadSuccess(IPage uploadPage)
    {
        await uploadPage.WaitForSelectorAsync("h2:has-text('Invoice Uploaded Successfully!')");
        await uploadPage.CloseAsync();
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
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        await Page.ScreenshotAsync(new PageScreenshotOptions { Path = "invoice-upload-result.png" });
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
        // Navigate back to invoice list page
        await GoToUrl($"/plugins/{storeId}/vendorpay/list");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    private async Task ConfigureAccountlessUploadSettings(string storeId, string uploadCode, string descriptionQuestion)
    {
        await GoToUrl($"/plugins/{storeId}/vendorpay/settings");

        var invoiceOptionalToggle = Page.Locator("#MakeInvoiceFileOptional");
        if (!await invoiceOptionalToggle.IsCheckedAsync())
            await invoiceOptionalToggle.CheckAsync();

        var accountlessToggle = Page.Locator("#accountlessUploadToggle");
        if (!await accountlessToggle.IsCheckedAsync())
            await accountlessToggle.CheckAsync();

        await Page.FillAsync("#UploadCode", uploadCode);
        await Page.FillAsync("#DescriptionTitle", descriptionQuestion);

        await Page.Locator("#Edit").ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var statusText = (await FindAlertMessageAsync(StatusMessageModel.StatusSeverity.Success)).TextContentAsync();
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
            // Navigate back to invoice list page
            await GoToUrl($"/plugins/{storeId}/vendorpay/list");
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
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
