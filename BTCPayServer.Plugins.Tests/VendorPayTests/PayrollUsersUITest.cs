using Xunit;
using BTCPayServer.Tests;
using Xunit.Abstractions;
using NBitcoin;
using BTCPayServer.Abstractions.Models;

namespace BTCPayServer.Plugins.Tests.VendorPayTests
{
    public class PayrollUsersUITest : PlaywrightBaseTest, IClassFixture<VendorPayPluginServerTesterFixture>
    {
        public ServerTester ServerTester { get; private set; }
        public string TestDir { get; private set; }

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
            await Page.Locator("#createUserButton").ClickAsync();

            var user = RandomUtils.GetUInt256().ToString().Substring(64 - 20) + "@a.com";
            await Page.FillAsync("#Email", user);
            await Page.FillAsync("#Name", "TestUser");
            await Page.FillAsync("#Password", "123456");
            await Page.FillAsync("#ConfirmPassword", "123456");
            await Page.Locator("#Create").ClickAsync();
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
            await Page.Locator("#createUserButton").ClickAsync();

            var user = RandomUtils.GetUInt256().ToString().Substring(64 - 20) + "@a.com";
            await Page.FillAsync("#Email", user);
            await Page.FillAsync("#Name", "TestUser");
            await Page.FillAsync("#Password", "123456");
            await Page.FillAsync("#ConfirmPassword", "1234567");
            await Page.Locator("#Create").ClickAsync();
            Assert.True(await Page.Locator(".text-danger:has-text('Password fields don\\'t match')").IsVisibleAsync());
        }
    }

}

