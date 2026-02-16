using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Security;
using BTCPayServer.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace BTCPayServer.RockstarDev.Plugins.VendorPay.Security;

public class VendorPayAuthorizationHandler : AuthorizationHandler<PolicyRequirement>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly StoreRepository _storeRepository;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly PluginPermissionRegistry _pluginPermissionRegistry;

    public VendorPayAuthorizationHandler(
        IHttpContextAccessor httpContextAccessor,
        UserManager<ApplicationUser> userManager,
        StoreRepository storeRepository,
        PluginPermissionRegistry pluginPermissionRegistry)
    {
        _httpContextAccessor = httpContextAccessor;
        _userManager = userManager;
        _storeRepository = storeRepository;
        _pluginPermissionRegistry = pluginPermissionRegistry;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PolicyRequirement requirement)
    {
        // Handle any VendorPay plugin permission (generic approach)
        if (!requirement.Policy.StartsWith("btcpay.plugin.vendorpay.", System.StringComparison.OrdinalIgnoreCase))
            return;

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
            return;

        var user = await _userManager.GetUserAsync(context.User);
        if (user == null)
            return;

        var storeId = httpContext.GetImplicitStoreId();
        if (string.IsNullOrEmpty(storeId))
            return;

        var store = await _storeRepository.FindStore(storeId, user.Id);
        if (store == null)
            return;

        // Set store data in HTTP context so it's available to controllers/views
        httpContext.SetStoreData(store);

        // Check if user has the plugin permission in their role
        var storeRole = store.GetStoreRoleOfUser(user.Id);
        if (storeRole?.Permissions != null &&
            _pluginPermissionRegistry.IsSatisfiedByGrantedPolicies(requirement.Policy, storeRole.Permissions))
        {
            context.Succeed(requirement);
        }
    }
}
