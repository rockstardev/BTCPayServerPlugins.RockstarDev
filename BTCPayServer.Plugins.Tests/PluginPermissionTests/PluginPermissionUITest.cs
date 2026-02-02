using System;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Tests;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Plugins.Tests;

[Collection("Plugin Tests")]
[Trait("Category", "PlaywrightUITest")]
public class PluginPermissionUITest : PlaywrightBaseTest
{
    private readonly SharedPluginTestFixture _fixture;

    public PluginPermissionUITest(SharedPluginTestFixture fixture, ITestOutputHelper helper) : base(helper)
    {
        _fixture = fixture;
        if (_fixture.ServerTester == null) _fixture.Initialize(this);
        ServerTester = _fixture.ServerTester;
    }

    public ServerTester ServerTester { get; }

    [Fact]
    public async Task ServerRolesPage_DisplaysPluginPermissions()
    {
        await InitializePlaywright(ServerTester);
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin();
        
        await GoToUrl("/login");
        await LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
        
        // Navigate directly to Server Roles Owner page
        await GoToUrl("/server/roles/Owner");
        
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        
        // Verify we're on the role edit page
        var rolePageTitle = await Page.Locator("h1, h2").First.TextContentAsync();
        TestLogs.LogInformation($"Role page title: {rolePageTitle}");
        
        // Look for "Permissions" section
        var permissionsHeading = Page.Locator("h3, h4, h5").Locator("text=/Permissions/i");
        if (await permissionsHeading.CountAsync() > 0)
        {
            TestLogs.LogInformation("Found Permissions heading");
        }
        
        // Look for "Modify your stores" permission section
        var modifyStoresSection = Page.Locator("text=/Modify your stores/i");
        var modifyStoresExists = await modifyStoresSection.CountAsync() > 0;
        TestLogs.LogInformation($"'Modify your stores' section exists: {modifyStoresExists}");
        
        if (modifyStoresExists)
        {
            // Get all permission checkboxes
            var allCheckboxes = Page.Locator("input.policy-cb");
            var checkboxCount = await allCheckboxes.CountAsync();
            TestLogs.LogInformation($"Total permission checkboxes found: {checkboxCount}");
            
            // Get all permission labels
            var allLabels = Page.Locator("label");
            var labelCount = await allLabels.CountAsync();
            TestLogs.LogInformation($"Total labels found: {labelCount}");
            
            // Log all permission text for debugging
            for (int i = 0; i < Math.Min(labelCount, 50); i++)
            {
                var labelText = await allLabels.Nth(i).TextContentAsync();
                if (!string.IsNullOrWhiteSpace(labelText))
                {
                    TestLogs.LogInformation($"Permission {i}: {labelText.Trim()}");
                }
            }
            
            // Look for plugin permissions (should appear after "Modify your stores" section)
            // Plugin permissions should have format like "btcpay.plugin.{pluginname}.{action}"
            var pluginPermissionCheckboxes = Page.Locator("input.policy-cb[value^='btcpay.plugin.']");
            var pluginPermissionCount = await pluginPermissionCheckboxes.CountAsync();
            TestLogs.LogInformation($"Plugin permission checkboxes found: {pluginPermissionCount}");
            
            // Verify the page structure is correct for plugin permissions to be added
            // The permissions form should exist
            var permissionsForm = Page.Locator("form");
            Assert.True(await permissionsForm.CountAsync() > 0, "Permissions form should exist");
            
            // Verify we have core store permissions (server roles can assign store permissions)
            // The permission value format is like "btcpay.store.canmodifystoresettings"
            var storePermissionCheckboxes = Page.Locator("input.policy-cb[value^='btcpay.store']");
            var storePermissionCount = await storePermissionCheckboxes.CountAsync();
            TestLogs.LogInformation($"Found {storePermissionCount} store permission checkboxes");
            Assert.True(storePermissionCount > 0, "Core store permissions should exist on server roles page");
            
            // Verify plugin permissions are now visible (feature implemented!)
            // The VendorPay plugin should register its permission
            TestLogs.LogInformation($"Plugin permissions found: {pluginPermissionCount}");
            Assert.True(pluginPermissionCount > 0, "Plugin permissions should be visible now that feature is implemented");
            
            // Verify we can find the VendorPay plugin permission
            var vendorPayManagePermission = Page.Locator("input.policy-cb[value='btcpay.plugin.vendorpay.canmanage']");
            var vendorPayManageExists = await vendorPayManagePermission.CountAsync() > 0;
            TestLogs.LogInformation($"VendorPay plugin 'manage' permission exists: {vendorPayManageExists}");
            Assert.True(vendorPayManageExists, "VendorPay plugin permission should be visible");
            
            if (vendorPayManageExists)
            {
                // Verify the display name is shown correctly
                var manageLabel = Page.Locator("label[for='Policy-btcpay_plugin_vendorpay_canmanage']");
                if (await manageLabel.CountAsync() > 0)
                {
                    var labelText = await manageLabel.TextContentAsync();
                    TestLogs.LogInformation($"VendorPay plugin 'manage' permission label: {labelText}");
                    Assert.Contains("Vendor Pay", labelText);
                }
            }
            
            // Take a screenshot for visual verification
            await Page.ScreenshotAsync(new PageScreenshotOptions 
            { 
                Path = $"server-roles-permissions-{DateTime.UtcNow:yyyyMMdd-HHmmss}.png",
                FullPage = true
            });
            
            TestLogs.LogInformation("Test completed successfully - page structure verified, ready for plugin permissions implementation");
        }
        else
        {
            // If we can't find the permissions section, log the page content for debugging
            var pageContent = await Page.ContentAsync();
            TestLogs.LogInformation($"Page content length: {pageContent.Length}");
            
            // Take a screenshot
            await Page.ScreenshotAsync(new PageScreenshotOptions 
            { 
                Path = $"server-roles-page-{DateTime.UtcNow:yyyyMMdd-HHmmss}.png",
                FullPage = true
            });
            
            Assert.Fail("Could not find 'Modify your stores' permission section on the page");
        }
    }
}
