using System.Collections.Generic;
using BTCPayServer.Abstractions.Contracts;

namespace BTCPayServer.RockstarDev.Plugins.VendorPay.Security;

public static class VendorPayPermissions
{
    // Policy strings
    public const string Admin = "btcpay.plugin.vendorpay.admin";
    public const string UsersManage = "btcpay.plugin.vendorpay.users.manage";
    public const string InvoicesManage = "btcpay.plugin.vendorpay.invoices.manage";
    public const string InvoicesView = "btcpay.plugin.vendorpay.invoices.view";
    public const string SettingsManage = "btcpay.plugin.vendorpay.settings.manage";

    public static readonly string[] AllPolicyNames =
    [
        Admin,
        UsersManage,
        InvoicesManage,
        InvoicesView,
        SettingsManage
    ];

    // Full permission definitions for registration
    public static IEnumerable<PluginPermission> AllPermissions(string pluginIdentifier)
    {
        return new[]
        {
            new PluginPermission
            {
                Policy = Admin,
                DisplayName = "Vendor Pay: Admin",
                Description = "Full administration of Vendor Pay users, invoices, and settings.",
                PluginIdentifier = pluginIdentifier,
                ChildPolicies = new List<PluginPermission>
                {
                    new PluginPermission
                    {
                        Policy = UsersManage,
                        DisplayName = "Vendor Pay: Manage users",
                        Description = "Create, update, disable, reset, and remove Vendor Pay users.",
                        PluginIdentifier = pluginIdentifier
                    },
                    new PluginPermission
                    {
                        Policy = InvoicesManage,
                        DisplayName = "Vendor Pay: Manage invoices",
                        Description = "Upload, pay, mark paid, delete, and otherwise action Vendor Pay invoices.",
                        PluginIdentifier = pluginIdentifier,
                        ChildPolicies = new List<PluginPermission>
                        {
                            new PluginPermission
                            {
                                Policy = InvoicesView,
                                DisplayName = "Vendor Pay: View invoices",
                                Description = "View and download Vendor Pay invoices.",
                                PluginIdentifier = pluginIdentifier
                            },
                        }
                    },
                    new PluginPermission
                    {
                        Policy = SettingsManage,
                        DisplayName = "Vendor Pay: Manage settings",
                        Description = "View and update Vendor Pay settings and notification templates.",
                        PluginIdentifier = pluginIdentifier
                    }
                }
            },
        };
    }
}
