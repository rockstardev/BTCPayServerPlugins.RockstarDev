using Xunit;
using BTCPayServer.Tests;
using Xunit.Abstractions;
using NBitcoin;
using BTCPayServer.Abstractions.Models;
using Microsoft.Playwright;
using System.Text.RegularExpressions;
using static QRCoder.PayloadGenerator;

namespace BTCPayServer.Plugins.Tests.VendorPayTests
{
    public class PayrollUsersUITest : PlaywrightBaseTest, IClassFixture<VendorPayPluginServerTesterFixture>
    {
        public ServerTester ServerTester { get; private set; }
        public string TestDir { get; private set; }
        public string PayrollUserName { get; private set; }

        private readonly VendorPayPluginServerTesterFixture _fixture;

        public PayrollUsersUITest(VendorPayPluginServerTesterFixture fixture, ITestOutputHelper helper) : base(helper)
        {
            _fixture = fixture;
            if (_fixture.ServerTester == null)
            {
                _fixture.Initialize(this);
            }
            ServerTester = _fixture.ServerTester;
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
            await Page.Locator("#Edit").ClickAsync();

            var statusText = (await FindAlertMessageAsync(expectedSeverity)).TextContentAsync();
            bool isEdited = expectedSeverity == StatusMessageModel.StatusSeverity.Success &&
                    (await statusText).Trim() == "User details updated successfully";
            Assert.True(isEdited);
            await Page.WaitForSelectorAsync($"text={newName}");
            var newRecordCount = await Page.Locator("tr", new() { HasTextString = newName }).CountAsync();
            Assert.True(newRecordCount > 0, "Updated user row not found.");

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
    }

}

