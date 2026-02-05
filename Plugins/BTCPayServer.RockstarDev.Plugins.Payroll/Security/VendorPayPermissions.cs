using System.Collections.Generic;
using BTCPayServer.Abstractions.Contracts;

namespace BTCPayServer.RockstarDev.Plugins.VendorPay.Security;

public static class VendorPayPermissions
{
    // Policy strings
    public const string CanManageVendorPay = "btcpay.plugin.vendorpay.canmanage";

    // Full permission definitions for registration
    // public static IEnumerable<PluginPermission> AllPermissions(string pluginIdentifier)
    // {
    //     return new[]
    //     {
    //         new PluginPermission
    //         {
    //             Policy = CanManageVendorPay,
    //             DisplayName = "Vendor Pay: Manage",
    //             Description = "Full management of vendor payments including creating invoices, managing users, and viewing all vendor payment data",
    //             PluginIdentifier = pluginIdentifier,
    //             Scope = PermissionScope.Store
    //         }
    //     };
    // }
}
