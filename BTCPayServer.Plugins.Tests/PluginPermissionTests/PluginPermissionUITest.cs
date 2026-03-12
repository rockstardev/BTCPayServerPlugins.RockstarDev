using System;
using System.Linq;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
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
    private const string VendorPayAdminPolicy = "btcpay.store.vendorpay.admin";
    private const string VendorPayInvoicesManagePolicy = "btcpay.store.vendorpay.manageinvoices";
    private const string VendorPayInvoicesViewPolicy = "btcpay.store.vendorpay.viewinvoices";
    private const string VendorPayUsersManagePolicy = "btcpay.store.vendorpay.manageusers";
    private const string VendorPaySettingsManagePolicy = "btcpay.store.vendorpay.managesettings";
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

        // Verify VendorPay permission is visible
        var vendorPayCheckbox = Page.Locator($"input.policy-cb[value='{VendorPayAdminPolicy}']");
        var vendorPayExists = await vendorPayCheckbox.CountAsync() > 0;
        Assert.True(vendorPayExists, "VendorPay plugin permission should be visible");

        // Verify display name
        var manageLabel = Page.Locator($"label[for='Policy-{VendorPayAdminPolicy.Replace(".", "_")}']");
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
        Assert.True(await invoicesManageCheckbox.CountAsync() > 0, "VendorPay invoices manage checkbox should be visible");
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
    public void VendorPayPermissions_AreRegisteredInPermissionService()
    {
        // Verify that VendorPay PolicyDefinitions are registered in the PermissionService
        var permissionService = ServerTester.PayTester.GetService<PermissionService>();
        Assert.NotNull(permissionService);

        // Admin policy should be registered
        Assert.True(permissionService.TryGetDefinition(VendorPayAdminPolicy, out var adminDef));
        Assert.Equal("Vendor Pay: Admin", adminDef.Display.Title);

        // Admin should include child policies in the hierarchy
        var adminNode = permissionService.PermissionNodesByPolicy[VendorPayAdminPolicy];
        var childPolicies = adminNode.Children.Select(c => c.Definition.Policy).ToArray();
        Assert.Contains(VendorPayInvoicesManagePolicy, childPolicies);
        Assert.Contains(VendorPayUsersManagePolicy, childPolicies);
        Assert.Contains(VendorPaySettingsManagePolicy, childPolicies);

        TestLogs.LogInformation($"VendorPay Admin permission found: {adminDef.Policy}");
        TestLogs.LogInformation($"Display name: {adminDef.Display.Title}");
        TestLogs.LogInformation($"Child policies: {string.Join(", ", childPolicies)}");
    }

    [Fact]
    public void PermissionService_ContainsAllVendorPayPermissions()
    {
        // Verify that all VendorPay permissions are registered in the PermissionService
        var permissionService = ServerTester.PayTester.GetService<PermissionService>();
        Assert.NotNull(permissionService);

        Assert.True(permissionService.IsValidPolicy(VendorPayAdminPolicy), $"{VendorPayAdminPolicy} should be registered");
        Assert.True(permissionService.IsValidPolicy(VendorPayInvoicesManagePolicy), $"{VendorPayInvoicesManagePolicy} should be registered");
        Assert.True(permissionService.IsValidPolicy(VendorPayInvoicesViewPolicy), $"{VendorPayInvoicesViewPolicy} should be registered");
        Assert.True(permissionService.IsValidPolicy(VendorPayUsersManagePolicy), $"{VendorPayUsersManagePolicy} should be registered");
        Assert.True(permissionService.IsValidPolicy(VendorPaySettingsManagePolicy), $"{VendorPaySettingsManagePolicy} should be registered");

        TestLogs.LogInformation("Verified: All VendorPay permissions are properly registered in PermissionService");
    }

    [Fact]
    public void VendorPayPermissionHierarchy_InvoicesManageIncludesInvoicesView()
    {
        var permissionService = ServerTester.PayTester.GetService<PermissionService>();
        Assert.NotNull(permissionService);

        // invoicesManage should include invoicesView as a child
        var invoicesManageNode = permissionService.PermissionNodesByPolicy[VendorPayInvoicesManagePolicy];
        var childPolicies = invoicesManageNode.Children.Select(c => c.Definition.Policy).ToArray();
        Assert.Contains(VendorPayInvoicesViewPolicy, childPolicies);

        // invoicesView should enumerate invoicesManage as a parent
        var invoicesViewNode = permissionService.PermissionNodesByPolicy[VendorPayInvoicesViewPolicy];
        var parentPolicies = invoicesViewNode.EnumerateParents(includeSelf: false).Select(p => p.Definition.Policy).ToArray();
        Assert.Contains(VendorPayInvoicesManagePolicy, parentPolicies);
    }

    [Fact]
    public void VendorPayAdmin_IsIncludedByStoreOwner()
    {
        // Store owners (CanModifyStoreSettings) should automatically have VendorPay Admin access
        var permissionService = ServerTester.PayTester.GetService<PermissionService>();
        Assert.NotNull(permissionService);

        var adminNode = permissionService.PermissionNodesByPolicy[VendorPayAdminPolicy];
        var parentPolicies = adminNode.EnumerateParents(includeSelf: false).Select(p => p.Definition.Policy).ToArray();
        Assert.Contains(Policies.CanModifyStoreSettings, parentPolicies);

        TestLogs.LogInformation("Verified: VendorPay Admin is included by CanModifyStoreSettings");
    }

    [Fact]
    public async Task PluginPermission_SavedAndDisplayedCorrectly()
    {
        /*
         * TEST ASSUMPTIONS:
         * 1. VendorPay plugin is installed and registers permission via PolicyDefinition
         * 2. Plugin permissions appear in the Permissions section on role edit page
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

        // Wait for the page to fully load and render permissions
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
        var selectElement = Page.Locator("#Permissions");
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
        var permissionLabel = Page.Locator($"label[for='Policy-{VendorPayAdminPolicy.Replace(".", "_")}']");
        Assert.True(await permissionLabel.CountAsync() > 0, "Permission label should exist");

        var labelText = await permissionLabel.TextContentAsync() ?? string.Empty;
        TestLogs.LogInformation($"Permission label text: {labelText}");

        // Should show "Vendor Pay: Admin"
        Assert.Contains("Vendor Pay", labelText);
    }

    [Fact]
    public async Task StoreRolesPage_DisplaysPluginPermissions()
    {
        /*
         * TEST ASSUMPTIONS:
         * 1. Store roles page should display plugin permissions alongside built-in permissions
         * 2. VendorPay permission should be visible and checkable
         * 3. Plugin permissions should be saved to store roles
         * 4. Store-scoped plugin permissions should work the same as server-scoped
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

        // Wait for permissions to load
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

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

    [Fact]
    public async Task StoreOwner_CanAccessAllVendorPayPages_WithoutExplicitPluginPermission()
    {
        await InitializePlaywright(ServerTester);

        // Store owner has CanModifyStoreSettings via the default Owner role - no plugin permissions assigned
        var owner = ServerTester.NewAccount();
        await owner.GrantAccessAsync();
        var storeId = owner.StoreId;

        await GoToUrl("/login");
        await LogIn(owner.RegisterDetails.Email, owner.RegisterDetails.Password);

        // Navigate to store to verify nav is visible
        await GoToUrl($"/stores/{storeId}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var vendorPayNavItem = Page.Locator("li.nav-item a[href*='/vendorpay']");
        var navCount = await vendorPayNavItem.CountAsync();
        TestLogs.LogInformation($"Store owner VendorPay nav items: {navCount}");
        Assert.True(navCount > 0, "Store owner should see VendorPay nav items");

        // Verify access to all VendorPay endpoints
        foreach (var url in new[]
                 {
                     $"/plugins/{storeId}/vendorpay/list",
                     $"/plugins/{storeId}/vendorpay/users/list",
                     $"/plugins/{storeId}/vendorpay/settings"
                 })
        {
            await GoToUrl(url);
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            TestLogs.LogInformation($"Store owner -> {url}: {Page.Url}");
            AssertNotDenied(Page.Url, url);
        }

    }

    [Fact]
    public async Task Hierarchy_AdminPolicy_GrantsAccessToAllVendorPayPages()
    {
        var (storeId, _) = await CreateUserWithPluginRole(VendorPayAdminPolicy);

        // Admin should access every VendorPay endpoint
        // Child policy tests
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

        // invoices.view is the leaf - should access the invoice list
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

    // -- Helpers --

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
