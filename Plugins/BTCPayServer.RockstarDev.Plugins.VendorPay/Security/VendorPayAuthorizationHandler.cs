using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Security;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace BTCPayServer.RockstarDev.Plugins.VendorPay.Security;

public class VendorPayAuthorizationHandler : AuthorizationHandler<PolicyRequirement>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly StoreRepository _storeRepository;

    public VendorPayAuthorizationHandler(
        IHttpContextAccessor httpContextAccessor,
        UserManager<ApplicationUser> userManager,
        StoreRepository storeRepository)
    {
        _httpContextAccessor = httpContextAccessor;
        _userManager = userManager;
        _storeRepository = storeRepository;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PolicyRequirement requirement)
    {
        if (requirement.Policy != VendorPayPolicies.CanManageVendorPay)
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

        // Check if user has CanModifyStoreSettings permission
        if (store.HasPermission(user.Id, Policies.CanModifyStoreSettings))
        {
            context.Succeed(requirement);
            return;
        }

        // Check if user has CanViewStoreSettings permission (workaround - appears in UI)
        if (store.HasPermission(user.Id, Policies.CanViewStoreSettings))
        {
            context.Succeed(requirement);
            return;
        }

        // Check if user has the custom VendorPay permission (for future use)
        var storeRole = store.GetStoreRoleOfUser(user.Id);
        if (storeRole?.Permissions != null && 
            storeRole.Permissions.Contains(VendorPayPolicies.CanManageVendorPay))
        {
            context.Succeed(requirement);
            return;
        }
    }
}
