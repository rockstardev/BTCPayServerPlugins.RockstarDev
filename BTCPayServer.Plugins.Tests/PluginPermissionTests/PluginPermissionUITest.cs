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

    [Fact]
    public async Task UserWithoutVendorPayPermission_CannotSeeOrAccessVendorPay()
    {
        await InitializePlaywright(ServerTester);
        
        // Create admin user and store
        var admin = ServerTester.NewAccount();
        await admin.GrantAccessAsync();
        await admin.MakeAdmin(true); // Make server admin
        var storeId = admin.StoreId;
        
        // Login as admin
        await GoToUrl("/login");
        await LogIn(admin.RegisterDetails.Email, admin.RegisterDetails.Password);
        
        // Create a custom role without VendorPay permission
        var customRoleName = $"TestRole_{System.Guid.NewGuid():N}"[..15];
        await GoToUrl($"/stores/{storeId}/roles");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        
        // Click the "Create Role" link/button
        var createRoleButton = Page.Locator("a[href*='/roles/create'], button:has-text('Create Role')");
        await createRoleButton.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        
        await Page.FillAsync("#Role", customRoleName);
        
        // Check some basic store permissions but NOT VendorPay permission
        await Page.Locator("input.policy-cb[value='btcpay.store.canviewstoresettings']").First.CheckAsync();
        await Page.Locator("input.policy-cb[value='btcpay.store.canviewinvoices']").First.CheckAsync();
        
        // Make sure VendorPay permission is NOT checked
        var vendorPayCheckbox = Page.Locator("input.policy-cb[value='btcpay.plugin.vendorpay.canmanage']");
        if (await vendorPayCheckbox.CountAsync() > 0)
        {
            await vendorPayCheckbox.UncheckAsync();
        }
        
        await Page.Locator("button[type='submit']").ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        
        TestLogs.LogInformation($"Created custom role: {customRoleName}");
        
        // Create a server user first
        var restrictedUserEmail = $"restricted-{System.Guid.NewGuid():N}"[..20] + "@test.com";
        var restrictedUserPassword = "TestPassword123!";
        
        await GoToUrl("/server/users");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.Locator("a[href='/server/users/new']").ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        
        await Page.FillAsync("#Email", restrictedUserEmail);
        await Page.FillAsync("#Password", restrictedUserPassword);
        await Page.FillAsync("#ConfirmPassword", restrictedUserPassword);
        await Page.Locator("button[type='submit']").ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        
        TestLogs.LogInformation($"Created server user: {restrictedUserEmail}");
        
        // Now add the user to the store with the restricted role
        await GoToUrl($"/stores/{storeId}/users");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        
        await Page.FillAsync("input[placeholder='user@example.com']", restrictedUserEmail);
        await Page.Locator("#Role").First.SelectOptionAsync(customRoleName);
        await Page.Locator("button#AddUser").ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        
        TestLogs.LogInformation($"Added user to store with role {customRoleName}");
        
        // Login as the restricted user
        await Page.Context.ClearCookiesAsync();
        await GoToUrl("/login");
        await LogIn(restrictedUserEmail, restrictedUserPassword);
        
        // Navigate to the store page
        await GoToUrl($"/stores/{storeId}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        
        // Verify VendorPay nav item is NOT visible in the sidebar
        var vendorPayNavItem = Page.Locator("li.nav-item a[href*='/vendorpay']");
        var navItemCount = await vendorPayNavItem.CountAsync();
        TestLogs.LogInformation($"VendorPay nav items found: {navItemCount}");
        Assert.Equal(0, navItemCount);
        
        // Verify VendorPayNav section is not rendered at all (permission wrapper prevents rendering)
        var pageContent = await Page.ContentAsync();
        var hasVendorPayNavSection = pageContent.Contains("Vendor Pay") && pageContent.Contains("vendorpay");
        TestLogs.LogInformation($"VendorPayNav section rendered: {hasVendorPayNavSection}");
        Assert.False(hasVendorPayNavSection, "VendorPayNav section should not be rendered for users without permission");
        
        // Try to directly navigate to VendorPay pages - should get 403 or redirect
        var vendorPayUrls = new[]
        {
            $"/plugins/{storeId}/vendorpay/list",
            $"/plugins/{storeId}/vendorpay/users/list",
            $"/plugins/{storeId}/vendorpay/settings"
        };
        
        foreach (var url in vendorPayUrls)
        {
            TestLogs.LogInformation($"Attempting to access: {url}");
            await GoToUrl(url);
            
            var currentUrl = Page.Url;
            TestLogs.LogInformation($"Current URL after navigation: {currentUrl}");
            
            // Either we're redirected to an error/access denied page or don't end up on vendorpay
            var isAccessDenied = currentUrl.Contains("/error") ||
                                 currentUrl.Contains("/login") ||
                                 !currentUrl.Contains("/vendorpay");
            
            Assert.True(isAccessDenied, $"User should not have access to {url}. Current URL: {currentUrl}");
        }
        
        TestLogs.LogInformation("Verified: User without VendorPay permission cannot see or access VendorPay pages");
    }
}
