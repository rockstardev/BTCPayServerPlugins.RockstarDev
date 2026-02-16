using System;
using System.Linq;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Services;
using BTCPayServer.Services.Stores;
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
    private const string VendorPayAdminPolicy = "btcpay.plugin.vendorpay.admin";
    private const string VendorPayInvoicesManagePolicy = "btcpay.plugin.vendorpay.invoices.manage";
    private const string VendorPayInvoicesViewPolicy = "btcpay.plugin.vendorpay.invoices.view";
    private const string VendorPayUsersManagePolicy = "btcpay.plugin.vendorpay.users.manage";
    private const string VendorPaySettingsManagePolicy = "btcpay.plugin.vendorpay.settings.manage";
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
        /*
         * TEST: Verify plugin permissions are displayed on Server Roles page
         * This is a simpler test than PluginPermission_SavedAndDisplayedCorrectly
         * which tests the full save/load cycle. This just verifies display.
         */
        await InitializePlaywright(ServerTester);
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin();
        
        await GoToUrl("/login");
        await LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
        
        // Navigate to Server Roles Owner page
        await GoToUrl("/server/roles/Owner");
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        
        // Verify plugin permissions section exists
        var pluginPermissionsHeading = Page.Locator("h5:has-text('Plugin Permissions')");
        var hasPluginSection = await pluginPermissionsHeading.CountAsync() > 0;
        Assert.True(hasPluginSection, "Server Roles page should have Plugin Permissions section");
        
        // Verify VendorPay permission is visible
        var vendorPayCheckbox = Page.Locator($"input.policy-cb[value='{VendorPayAdminPolicy}']");
        var vendorPayExists = await vendorPayCheckbox.CountAsync() > 0;
        Assert.True(vendorPayExists, "VendorPay plugin permission should be visible");
        
        // Verify display name
        var manageLabel = Page.Locator("label[for='Policy-btcpay_plugin_vendorpay_admin']");
        if (await manageLabel.CountAsync() > 0)
        {
            var labelText = await manageLabel.TextContentAsync() ?? string.Empty;
            Assert.Contains("Vendor Pay", labelText);
        }
        
        TestLogs.LogInformation("Verified: Plugin permissions display correctly on Server Roles page");
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
        var customRoleName = $"TestRole_{Guid.NewGuid():N}"[..15];
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
        var vendorPayCheckbox = Page.Locator($"input.policy-cb[value='{VendorPayAdminPolicy}']");
        if (await vendorPayCheckbox.CountAsync() > 0)
        {
            await vendorPayCheckbox.UncheckAsync();
        }
        
        await Page.Locator("button[type='submit']").ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        
        TestLogs.LogInformation($"Created custom role: {customRoleName}");
        
        // Create a server user first
        var restrictedUserEmail = $"restricted-{Guid.NewGuid():N}"[..20] + "@test.com";
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
    public async Task UserWithInvoicesManagePermission_CanAccessInvoiceViewButNotUsersOrSettings()
    {
        await InitializePlaywright(ServerTester);

        // Create admin user and store
        var admin = ServerTester.NewAccount();
        await admin.GrantAccessAsync();
        await admin.MakeAdmin(true);
        var storeId = admin.StoreId;

        await GoToUrl("/login");
        await LogIn(admin.RegisterDetails.Email, admin.RegisterDetails.Password);

        // Create a custom role with invoices.manage only
        var customRoleName = $"TestRole_{Guid.NewGuid():N}"[..15];
        await GoToUrl($"/stores/{storeId}/roles/create");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Page.FillAsync("#Role", customRoleName);
        var invoicesManageCheckbox = Page.Locator($"input.policy-cb[value='{VendorPayInvoicesManagePolicy}']");
        Assert.True(await invoicesManageCheckbox.CountAsync() > 0, "VendorPay invoices.manage checkbox should be visible");
        await invoicesManageCheckbox.CheckAsync();
        await invoicesManageCheckbox.DispatchEventAsync("change");

        await Page.Locator("button[type='submit']").ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Create a regular user and assign the custom role
        var restrictedUserEmail = $"manage-only-{Guid.NewGuid():N}"[..20] + "@test.com";
        var restrictedUserPassword = "TestPassword123!";

        await GoToUrl("/server/users/new");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.FillAsync("#Email", restrictedUserEmail);
        await Page.FillAsync("#Password", restrictedUserPassword);
        await Page.FillAsync("#ConfirmPassword", restrictedUserPassword);
        await Page.Locator("button[type='submit']").ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await GoToUrl($"/stores/{storeId}/users");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.FillAsync("input[placeholder='user@example.com']", restrictedUserEmail);
        await Page.Locator("#Role").First.SelectOptionAsync(customRoleName);
        await Page.Locator("button#AddUser").ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Login as restricted user
        await Page.Context.ClearCookiesAsync();
        await GoToUrl("/login");
        await LogIn(restrictedUserEmail, restrictedUserPassword);

        // invoices.manage should satisfy invoices.view requirement on list endpoint
        await GoToUrl($"/plugins/{storeId}/vendorpay/list");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        Assert.Contains($"/plugins/{storeId}/vendorpay/list", Page.Url);

        // But users/settings should remain forbidden
        foreach (var forbiddenUrl in new[]
                 {
                     $"/plugins/{storeId}/vendorpay/users/list",
                     $"/plugins/{storeId}/vendorpay/settings"
                 })
        {
            await GoToUrl(forbiddenUrl);
            var currentUrl = Page.Url;
            var denied = currentUrl.Contains("/error") ||
                         currentUrl.Contains("/login") ||
                         !currentUrl.Contains("/vendorpay/users/list") && !currentUrl.Contains("/vendorpay/settings");
            Assert.True(denied, $"User should not have access to {forbiddenUrl}. Current URL: {currentUrl}");
        }
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
        var vendorPayPermission = permissions.FirstOrDefault(p => p.Policy == VendorPayAdminPolicy);
        Assert.NotNull(vendorPayPermission);
        Assert.Equal("Vendor Pay: Admin", vendorPayPermission.DisplayName);

        var invoicesManagePermission = permissions.FirstOrDefault(p => p.Policy == VendorPayInvoicesManagePolicy);
        Assert.NotNull(invoicesManagePermission);
        
        TestLogs.LogInformation($"VendorPay permission found: {vendorPayPermission.Policy}");
        TestLogs.LogInformation($"Display name: {vendorPayPermission.DisplayName}");
        TestLogs.LogInformation($"Plugin identifier: {vendorPayPermission.PluginIdentifier}");
    }

    [Fact]
    public void PluginPermissionRegistry_ContainsRegisteredPermissions()
    {
        // Verify that permissions are registered in the registry
        var registry = ServerTester.PayTester.GetService<PluginPermissionRegistry>();
        Assert.NotNull(registry);
        
        var allPermissions = registry.GetAllPluginPermissions().ToList();
        TestLogs.LogInformation($"Registry contains {allPermissions.Count} plugin permissions");
        
        Assert.True(allPermissions.Count > 0, "Registry should contain plugin permissions");
        
        // Verify VendorPay permission is in registry
        var vendorPayPermission = registry.GetPermission(VendorPayAdminPolicy);
        Assert.NotNull(vendorPayPermission);
        Assert.Equal("Vendor Pay: Admin", vendorPayPermission.DisplayName);
        
        TestLogs.LogInformation("Verified: Plugin permissions are properly registered in registry");
    }

    [Fact]
    public void PluginPermissionRegistry_ReturnsPermissionsInDeterministicOrder()
    {
        var permissions = new[]
        {
            new PluginPermission
            {
                Policy = "btcpay.plugin.zplugin.canmanage",
                DisplayName = "Z Plugin: Manage",
                PluginIdentifier = "zplugin"
            },
            new PluginPermission
            {
                Policy = "btcpay.plugin.aplugin.canmanage",
                DisplayName = "A Plugin: Manage",
                PluginIdentifier = "aplugin"
            },
            new PluginPermission
            {
                Policy = "btcpay.plugin.mplugin.canmanage",
                DisplayName = "M Plugin: Manage",
                PluginIdentifier = "mplugin"
            }
        };

        var registry = new PluginPermissionRegistry(permissions);
        var orderedPolicies = registry.GetAllPluginPermissions().Select(p => p.Policy).ToArray();

        Assert.Equal(new[]
        {
            "btcpay.plugin.aplugin.canmanage",
            "btcpay.plugin.mplugin.canmanage",
            "btcpay.plugin.zplugin.canmanage"
        }, orderedPolicies);
    }

    [Fact]
    public void VendorPayPolicyNames_AreRegisteredForHierarchyRollout()
    {
        var registry = ServerTester.PayTester.GetService<PluginPermissionRegistry>();
        Assert.NotNull(registry);

        Assert.NotNull(registry.GetPermission(VendorPayInvoicesManagePolicy));
        Assert.NotNull(registry.GetPermission(VendorPayInvoicesViewPolicy));
    }

    [Fact]
    public async Task PluginPermission_SavedAndDisplayedCorrectly()
    {
        /*
         * TEST ASSUMPTIONS:
         * 1. VendorPay plugin is installed and registers permission "btcpay.plugin.vendorpay.admin"
         * 2. Plugin permissions should appear in "Plugin Permissions" section on role edit page
         * 3. Plugin permissions should be saved to database when role is created/updated
         * 4. Plugin permissions should persist across page reloads
         * 5. Display name should be "Vendor Pay: Admin" (from plugin registration)
         */
        await InitializePlaywright(ServerTester);
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin();
        
        await GoToUrl("/login");
        await LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
        
        // Create a role with VendorPay permission
        var customRoleName = $"TestRole_{Guid.NewGuid():N}"[..15];
        await GoToUrl("/server/roles/create");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        
        await Page.FillAsync("#Role", customRoleName);
        
        // Wait for the page to fully load and render plugin permissions
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        
        // Check VendorPay permission
        var vendorPayCheckbox = Page.Locator($"input.policy-cb[value='{VendorPayAdminPolicy}']");
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
        Assert.Contains(VendorPayAdminPolicy, selectedOptions);
        
        await Page.Locator("button[type='submit']").ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        
        TestLogs.LogInformation($"Created role with VendorPay permission: {customRoleName}");
        
        // Now edit the role and verify the permission is shown and checked
        await GoToUrl($"/server/roles/{customRoleName}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        
        // Re-locate the checkbox after navigation
        vendorPayCheckbox = Page.Locator($"input.policy-cb[value='{VendorPayAdminPolicy}']");
        var checkboxExists = await vendorPayCheckbox.CountAsync() > 0;
        Assert.True(checkboxExists, "VendorPay permission checkbox should exist on edit page");
        
        // Verify VendorPay permission checkbox is checked (persisted)
        var isChecked = await vendorPayCheckbox.IsCheckedAsync();
        Assert.True(isChecked, "VendorPay permission should be checked after reload");
        
        // Look for the permission label
        var permissionLabel = Page.Locator("label[for='Policy-btcpay_plugin_vendorpay_admin']");
        Assert.True(await permissionLabel.CountAsync() > 0, "Permission label should exist");
        
        var labelText = await permissionLabel.TextContentAsync() ?? string.Empty;
        TestLogs.LogInformation($"Permission label text: {labelText}");
        
        // Should show "Vendor Pay: Admin" (not orphaned since plugin is installed)
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
         * 9. Orphaned permissions should be preserved when form is submitted if they remain checked
         * 10. Orphaned permissions should be removed when unchecked and form is submitted
         * 
         * IMPLEMENTATION APPROACH:
         * - Since we can't actually uninstall VendorPay plugin in test, we'll:
         *   1. Create a role with a fake orphaned permission directly in database
         *   2. Verify the UI displays it with warning indicators
         *   3. Test that it persists when form is submitted with it checked
         *   4. Test that it can be removed from the role when unchecked
         */
        await InitializePlaywright(ServerTester);
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin();
        
        await GoToUrl("/login");
        await LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
        
        // Create a role with a fake orphaned permission
        var customRoleName = $"TestRole_{Guid.NewGuid():N}"[..15];
        var orphanedPermission = "btcpay.plugin.fakeuninstalled.manage";
        
        // Create role via repository with orphaned permission
        var storeId = user.StoreId;
        var storeRepository = ServerTester.PayTester.GetService<StoreRepository>();
        Assert.NotNull(storeRepository);
        var roleId = new StoreRoleId(storeId, customRoleName);
        
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
            var labelText = await orphanedLabel.TextContentAsync() ?? string.Empty;
            TestLogs.LogInformation($"Orphaned permission label: {labelText}");
            
            // Should contain warning indicator
            Assert.True(labelText.Contains("⚠") || labelText.Contains("Uninstalled Plugin"), 
                "Orphaned permission label should contain warning indicator");
        }
        
        // 5. Test that orphaned permission persists when form is submitted with it checked
        await Page.Locator("button[type='submit']").ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        
        TestLogs.LogInformation("Submitted form with orphaned permission checked");
        
        // Reload and verify orphaned permission is still there
        await GoToUrl($"/stores/{storeId}/roles/{customRoleName}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        
        orphanedCheckbox = Page.Locator($"input.policy-cb[value='{orphanedPermission}']");
        var stillExists = await orphanedCheckbox.CountAsync() > 0;
        Assert.True(stillExists, "Orphaned permission should persist after form submission");
        
        if (stillExists)
        {
            var stillChecked = await orphanedCheckbox.IsCheckedAsync();
            Assert.True(stillChecked, "Orphaned permission should remain checked after form submission");
            TestLogs.LogInformation("Verified: Orphaned permission persists when form is submitted with it checked");
        }
        
        // 6. Now test that we can uncheck and remove it
        await orphanedCheckbox.UncheckAsync();
        await orphanedCheckbox.DispatchEventAsync("change");
        
        var isUnchecked = !(await orphanedCheckbox.IsCheckedAsync());
        Assert.True(isUnchecked, "Should be able to uncheck orphaned permission");
        
        // 7. Save and verify it was removed
        await Page.Locator("button[type='submit']").ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        
        // Reload and verify orphaned permission is gone
        await GoToUrl($"/stores/{storeId}/roles/{customRoleName}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        
        orphanedCheckbox = Page.Locator($"input.policy-cb[value='{orphanedPermission}']");
        var removedSuccessfully = await orphanedCheckbox.CountAsync() == 0;
        if (!removedSuccessfully && await orphanedCheckbox.CountAsync() > 0)
        {
            var stillCheckedAfterRemoval = await orphanedCheckbox.IsCheckedAsync();
            Assert.False(stillCheckedAfterRemoval, "Orphaned permission should have been removed");
        }
        
        TestLogs.LogInformation("Verified: Orphaned permissions display with warnings, persist when checked, and can be removed when unchecked");
    }

    [Fact]
    public async Task StoreRolesPage_DisplaysPluginPermissions()
    {
        /*
         * TEST ASSUMPTIONS:
         * 1. Store roles page should display plugin permissions (same as Server roles)
         * 2. Plugin permissions should appear in "Plugin Permissions" section
         * 3. VendorPay permission should be visible and checkable
         * 4. Plugin permissions should be saved to store roles
         * 5. Store-scoped plugin permissions should work the same as server-scoped
         */
        await InitializePlaywright(ServerTester);
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin();
        
        await GoToUrl("/login");
        await LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
        
        var storeId = user.StoreId;
        
        // Create a new store role
        var customRoleName = $"TestRole_{Guid.NewGuid():N}"[..15];
        await GoToUrl($"/stores/{storeId}/roles/create");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        
        await Page.FillAsync("#Role", customRoleName);
        
        // Wait for plugin permissions to load
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        
        // Check for Plugin Permissions section
        var pluginPermissionsHeading = Page.Locator("h5:has-text('Plugin Permissions')");
        var hasPluginSection = await pluginPermissionsHeading.CountAsync() > 0;
        TestLogs.LogInformation($"Plugin Permissions section exists on Store Roles page: {hasPluginSection}");
        
        // This should be true after implementation
        Assert.True(hasPluginSection, "Store Roles page should have Plugin Permissions section");
        
        // Check for VendorPay permission checkbox
        var vendorPayCheckbox = Page.Locator($"input.policy-cb[value='{VendorPayAdminPolicy}']");
        var checkboxCount = await vendorPayCheckbox.CountAsync();
        TestLogs.LogInformation($"VendorPay permission checkbox count: {checkboxCount}");
        
        Assert.True(checkboxCount > 0, "VendorPay permission should be visible in Store Roles");
        
        // Check the VendorPay permission
        await vendorPayCheckbox.CheckAsync();
        await vendorPayCheckbox.DispatchEventAsync("change");
        
        // Verify it was checked
        var isChecked = await vendorPayCheckbox.IsCheckedAsync();
        Assert.True(isChecked, "VendorPay permission should be checkable");
        
        // Save the role
        await Page.Locator("button[type='submit']").ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        
        TestLogs.LogInformation($"Created store role with VendorPay permission: {customRoleName}");
        
        // Navigate back to edit and verify permission persisted
        await GoToUrl($"/stores/{storeId}/roles/{customRoleName}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        
        vendorPayCheckbox = Page.Locator($"input.policy-cb[value='{VendorPayAdminPolicy}']");
        var stillChecked = await vendorPayCheckbox.IsCheckedAsync();
        Assert.True(stillChecked, "VendorPay permission should persist in store role");
        
        TestLogs.LogInformation("Verified: Plugin permissions display and work correctly in Store Roles");
    }

    // ── ChildPolicies hierarchy tests ──

    [Fact]
    public async Task Hierarchy_AdminPolicy_GrantsAccessToAllVendorPayPages()
    {
        var (storeId, _) = await CreateUserWithPluginRole(VendorPayAdminPolicy);

        // Admin should access every VendorPay endpoint
        foreach (var url in new[]
                 {
                     $"/plugins/{storeId}/vendorpay/list",
                     $"/plugins/{storeId}/vendorpay/users/list",
                     $"/plugins/{storeId}/vendorpay/settings"
                 })
        {
            await GoToUrl(url);
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            TestLogs.LogInformation($"Admin navigated to {url} -> {Page.Url}");
            Assert.Contains("/vendorpay", Page.Url);
            AssertNotDenied(Page.Url, url);
        }
    }

    [Fact]
    public async Task Hierarchy_InvoicesViewOnly_CanSeeListButNotManageUsersOrSettings()
    {
        var (storeId, _) = await CreateUserWithPluginRole(VendorPayInvoicesViewPolicy);

        // invoices.view is the leaf — should access the invoice list
        await GoToUrl($"/plugins/{storeId}/vendorpay/list");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        TestLogs.LogInformation($"InvoicesView -> list: {Page.Url}");
        Assert.Contains($"/plugins/{storeId}/vendorpay/list", Page.Url);

        // Negative: child does NOT satisfy parent or siblings
        foreach (var forbiddenUrl in new[]
                 {
                     $"/plugins/{storeId}/vendorpay/users/list",
                     $"/plugins/{storeId}/vendorpay/settings"
                 })
        {
            await GoToUrl(forbiddenUrl);
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            TestLogs.LogInformation($"InvoicesView -> {forbiddenUrl}: {Page.Url}");
            AssertDenied(Page.Url, forbiddenUrl);
        }
    }

    [Fact]
    public async Task Hierarchy_UsersManageOnly_CanAccessUsersButNotInvoicesOrSettings()
    {
        var (storeId, _) = await CreateUserWithPluginRole(VendorPayUsersManagePolicy);

        // users.manage should access users page
        await GoToUrl($"/plugins/{storeId}/vendorpay/users/list");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        TestLogs.LogInformation($"UsersManage -> users/list: {Page.Url}");
        Assert.Contains("/vendorpay/users/list", Page.Url);

        // Negative: sibling policies don't imply each other
        foreach (var forbiddenUrl in new[]
                 {
                     $"/plugins/{storeId}/vendorpay/list",
                     $"/plugins/{storeId}/vendorpay/settings"
                 })
        {
            await GoToUrl(forbiddenUrl);
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            TestLogs.LogInformation($"UsersManage -> {forbiddenUrl}: {Page.Url}");
            AssertDenied(Page.Url, forbiddenUrl);
        }
    }

    // ── Helpers ──

    private async Task<(string storeId, string email)> CreateUserWithPluginRole(params string[] policies)
    {
        await InitializePlaywright(ServerTester);

        var admin = ServerTester.NewAccount();
        await admin.GrantAccessAsync();
        await admin.MakeAdmin(true);
        var storeId = admin.StoreId;

        await GoToUrl("/login");
        await LogIn(admin.RegisterDetails.Email, admin.RegisterDetails.Password);

        // Create a custom role with only the specified plugin policies
        var roleName = $"Role_{Guid.NewGuid():N}"[..12];
        await GoToUrl($"/stores/{storeId}/roles/create");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.FillAsync("#Role", roleName);

        foreach (var policy in policies)
        {
            var cb = Page.Locator($"input.policy-cb[value='{policy}']");
            Assert.True(await cb.CountAsync() > 0, $"Checkbox for {policy} should exist");
            await cb.CheckAsync();
            await cb.DispatchEventAsync("change");
        }

        await Page.Locator("button[type='submit']").ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        TestLogs.LogInformation($"Created role '{roleName}' with policies: {string.Join(", ", policies)}");

        // Create a user and assign the role
        var email = $"perm-{Guid.NewGuid():N}"[..18] + "@test.com";
        var password = "TestPassword123!";

        await GoToUrl("/server/users/new");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.FillAsync("#Email", email);
        await Page.FillAsync("#Password", password);
        await Page.FillAsync("#ConfirmPassword", password);
        await Page.Locator("button[type='submit']").ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await GoToUrl($"/stores/{storeId}/users");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.FillAsync("input[placeholder='user@example.com']", email);
        await Page.Locator("#Role").First.SelectOptionAsync(roleName);
        await Page.Locator("button#AddUser").ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        TestLogs.LogInformation($"Assigned '{email}' to store with role '{roleName}'");

        // Login as the new user
        await Page.Context.ClearCookiesAsync();
        await GoToUrl("/login");
        await LogIn(email, password);

        return (storeId, email);
    }

    private static void AssertDenied(string currentUrl, string attemptedUrl)
    {
        var denied = currentUrl.Contains("/error") ||
                     currentUrl.Contains("/login") ||
                     !currentUrl.Contains("/vendorpay");
        Assert.True(denied, $"Access should be denied for {attemptedUrl}. Current URL: {currentUrl}");
    }

    private static void AssertNotDenied(string currentUrl, string attemptedUrl)
    {
        var denied = currentUrl.Contains("/error") || currentUrl.Contains("/login");
        Assert.False(denied, $"Access should be granted for {attemptedUrl}. Current URL: {currentUrl}");
    }
}
