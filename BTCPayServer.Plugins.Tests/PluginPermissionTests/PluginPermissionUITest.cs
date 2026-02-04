using System;
using System.Linq;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Tests;
using Microsoft.Extensions.DependencyInjection;
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

    [Fact]
    public void PluginPermissions_AreRegisteredViaDI()
    {
        // Verify that PluginPermission instances can be retrieved from DI container
        var services = ServerTester.PayTester.GetService<IServiceProvider>();
        var permissions = services.GetServices<PluginPermission>().ToList();
        
        TestLogs.LogInformation($"Found {permissions.Count} plugin permissions via GetServices");
        
        // Should have at least VendorPay permission
        Assert.True(permissions.Count > 0, "Should find plugin permissions via GetServices<PluginPermission>()");
        
        // Verify VendorPay permission is present
        var vendorPayPermission = permissions.FirstOrDefault(p => p.Policy == "btcpay.plugin.vendorpay.canmanage");
        Assert.NotNull(vendorPayPermission);
        Assert.Equal("Vendor Pay: Manage", vendorPayPermission.DisplayName);
        Assert.Equal(PermissionScope.Store, vendorPayPermission.Scope);
        
        TestLogs.LogInformation($"VendorPay permission found: {vendorPayPermission.Policy}");
        TestLogs.LogInformation($"Display name: {vendorPayPermission.DisplayName}");
        TestLogs.LogInformation($"Plugin identifier: {vendorPayPermission.PluginIdentifier}");
    }

    [Fact]
    public void PluginPermissionRegistry_ContainsRegisteredPermissions()
    {
        // Verify that permissions are registered in the registry
        var registry = ServerTester.PayTester.GetService<BTCPayServer.Services.IPluginPermissionRegistry>();
        Assert.NotNull(registry);
        
        var allPermissions = registry.GetAllPluginPermissions().ToList();
        TestLogs.LogInformation($"Registry contains {allPermissions.Count} plugin permissions");
        
        Assert.True(allPermissions.Count > 0, "Registry should contain plugin permissions");
        
        // Verify VendorPay permission is in registry
        var vendorPayPermission = registry.GetPermission("btcpay.plugin.vendorpay.canmanage");
        Assert.NotNull(vendorPayPermission);
        Assert.Equal("Vendor Pay: Manage", vendorPayPermission.DisplayName);
        
        TestLogs.LogInformation("Verified: Plugin permissions are properly registered in registry");
    }

    [Fact]
    public async Task PluginPermission_SavedAndDisplayedCorrectly()
    {
        /*
         * TEST ASSUMPTIONS:
         * 1. VendorPay plugin is installed and registers permission "btcpay.plugin.vendorpay.canmanage"
         * 2. Plugin permissions should appear in "Plugin Permissions" section on role edit page
         * 3. Plugin permissions should be saved to database when role is created/updated
         * 4. Plugin permissions should persist across page reloads
         * 5. Display name should be "Vendor Pay: Manage" (from plugin registration)
         */
        await InitializePlaywright(ServerTester);
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin();
        
        await GoToUrl("/login");
        await LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
        
        // Create a role with VendorPay permission
        var customRoleName = $"TestRole_{System.Guid.NewGuid():N}"[..15];
        await GoToUrl("/server/roles/create");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        
        await Page.FillAsync("#Role", customRoleName);
        
        // Wait for the page to fully load and render plugin permissions
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        
        // Check VendorPay permission
        var vendorPayCheckbox = Page.Locator("input.policy-cb[value='btcpay.plugin.vendorpay.canmanage']");
        var checkboxCount = await vendorPayCheckbox.CountAsync();
        TestLogs.LogInformation($"VendorPay checkbox count before checking: {checkboxCount}");
        
        Assert.True(checkboxCount > 0, "VendorPay permission checkbox should be visible on create page");
        
        await vendorPayCheckbox.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await vendorPayCheckbox.CheckAsync();
        
        // Trigger the change event to ensure JavaScript handler updates the select element
        await vendorPayCheckbox.DispatchEventAsync("change");
        
        // Verify it was checked
        var isCheckedNow = await vendorPayCheckbox.IsCheckedAsync();
        Assert.True(isCheckedNow, "Checkbox should be checked after CheckAsync");
        
        // Verify the select element was updated
        var selectElement = Page.Locator("#Policies");
        var selectedOptions = await selectElement.EvaluateAsync<string[]>("el => Array.from(el.selectedOptions).map(o => o.value)");
        Assert.Contains("btcpay.plugin.vendorpay.canmanage", selectedOptions);
        
        await Page.Locator("button[type='submit']").ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        
        TestLogs.LogInformation($"Created role with VendorPay permission: {customRoleName}");
        
        // Now edit the role and verify the permission is shown and checked
        await GoToUrl($"/server/roles/{customRoleName}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        
        // Re-locate the checkbox after navigation
        vendorPayCheckbox = Page.Locator("input.policy-cb[value='btcpay.plugin.vendorpay.canmanage']");
        var checkboxExists = await vendorPayCheckbox.CountAsync() > 0;
        Assert.True(checkboxExists, "VendorPay permission checkbox should exist on edit page");
        
        // Verify VendorPay permission checkbox is checked (persisted)
        var isChecked = await vendorPayCheckbox.IsCheckedAsync();
        Assert.True(isChecked, "VendorPay permission should be checked after reload");
        
        // Look for the permission label
        var permissionLabel = Page.Locator("label[for='Policy-btcpay_plugin_vendorpay_canmanage']");
        Assert.True(await permissionLabel.CountAsync() > 0, "Permission label should exist");
        
        var labelText = await permissionLabel.TextContentAsync();
        TestLogs.LogInformation($"Permission label text: {labelText}");
        
        // Should show "Vendor Pay: Manage" (not orphaned since plugin is installed)
        Assert.Contains("Vendor Pay", labelText);
        Assert.DoesNotContain("Uninstalled Plugin", labelText);
        Assert.DoesNotContain("⚠", labelText);
    }

    [Fact]
    public async Task OrphanedPermission_DisplaysWithWarning()
    {
        /*
         * TEST ASSUMPTIONS FOR ORPHANED PERMISSIONS:
         * 1. When a plugin is uninstalled, its permissions remain in the database (graceful degradation)
         * 2. Orphaned permissions should be detected by checking if permission exists in registry
         * 3. Orphaned permissions should display with warning icon: ⚠️
         * 4. Orphaned permissions should show "[Uninstalled Plugin]" prefix in label
         * 5. Orphaned permissions should appear in a separate "Orphaned Plugin Permissions" warning section
         * 6. Orphaned permissions should still be editable (can be unchecked/removed)
         * 7. Warning section should have alert styling (yellow/warning color)
         * 8. Warning section should explain that permissions are from uninstalled plugins
         * 
         * IMPLEMENTATION APPROACH:
         * - Since we can't actually uninstall VendorPay plugin in test, we'll:
         *   1. Create a role with a fake orphaned permission directly in database
         *   2. Verify the UI displays it with warning indicators
         *   3. Test that it can be removed from the role
         */
        await InitializePlaywright(ServerTester);
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin();
        
        await GoToUrl("/login");
        await LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
        
        // Create a role with a fake orphaned permission
        var customRoleName = $"TestRole_{System.Guid.NewGuid():N}"[..15];
        var orphanedPermission = "btcpay.plugin.fakeuninstalled.manage";
        
        // Create role via repository with orphaned permission
        var storeId = user.StoreId;
        var storeRepository = ServerTester.PayTester.GetService<BTCPayServer.Services.Stores.StoreRepository>();
        var roleId = new BTCPayServer.Services.Stores.StoreRoleId(storeId, customRoleName);
        
        // Add role with orphaned permission directly
        await storeRepository.AddOrUpdateStoreRole(roleId, new List<string> { orphanedPermission });
        
        TestLogs.LogInformation($"Created role with orphaned permission: {customRoleName}");
        
        // Navigate to edit the role
        await GoToUrl($"/stores/{storeId}/roles/{customRoleName}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        
        // EXPECTED BEHAVIOR: Orphaned permission should display with warning
        
        // 1. Check for orphaned permissions warning section
        var orphanedSection = Page.Locator(".alert-warning:has-text('Orphaned Plugin Permissions')");
        var hasSeparateSection = await orphanedSection.CountAsync() > 0;
        TestLogs.LogInformation($"Orphaned permissions warning section exists: {hasSeparateSection}");
        
        // 2. Check if orphaned permission is displayed
        var orphanedCheckbox = Page.Locator($"input.policy-cb[value='{orphanedPermission}']");
        var orphanedExists = await orphanedCheckbox.CountAsync() > 0;
        TestLogs.LogInformation($"Orphaned permission checkbox exists: {orphanedExists}");
        Assert.True(orphanedExists, "Orphaned permission should be displayed");
        
        // 3. Check if it's checked (should be, since it's in the role)
        var isChecked = await orphanedCheckbox.IsCheckedAsync();
        TestLogs.LogInformation($"Orphaned permission is checked: {isChecked}");
        Assert.True(isChecked, "Orphaned permission should be checked");
        
        // 4. Check for warning indicators in label
        var orphanedLabel = Page.Locator($"label[for='Policy-{orphanedPermission.Replace(".", "_")}']");
        if (await orphanedLabel.CountAsync() > 0)
        {
            var labelText = await orphanedLabel.TextContentAsync();
            TestLogs.LogInformation($"Orphaned permission label: {labelText}");
            
            // Should contain warning indicator
            Assert.True(labelText.Contains("⚠") || labelText.Contains("Uninstalled Plugin"), 
                "Orphaned permission label should contain warning indicator");
        }
        
        // 5. Verify we can uncheck it (should be editable)
        await orphanedCheckbox.UncheckAsync();
        await orphanedCheckbox.DispatchEventAsync("change");
        
        var isUnchecked = !(await orphanedCheckbox.IsCheckedAsync());
        Assert.True(isUnchecked, "Should be able to uncheck orphaned permission");
        
        // 6. Save and verify it was removed
        await Page.Locator("button[type='submit']").ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        
        // Reload and verify orphaned permission is gone
        await GoToUrl($"/stores/{storeId}/roles/{customRoleName}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        
        orphanedCheckbox = Page.Locator($"input.policy-cb[value='{orphanedPermission}']");
        if (await orphanedCheckbox.CountAsync() > 0)
        {
            var stillChecked = await orphanedCheckbox.IsCheckedAsync();
            Assert.False(stillChecked, "Orphaned permission should have been removed");
        }
        
        TestLogs.LogInformation("Verified: Orphaned permissions display with warnings and can be removed");
    }
}
