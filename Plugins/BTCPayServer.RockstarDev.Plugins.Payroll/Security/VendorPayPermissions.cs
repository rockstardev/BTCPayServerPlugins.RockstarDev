using BTCPayServer.Client;
using BTCPayServer.Services;

namespace BTCPayServer.RockstarDev.Plugins.VendorPay.Security;

public static class VendorPayPermissions
{
    public const string Admin = "btcpay.store.vendorpay.admin";
    public const string UsersManage = "btcpay.store.vendorpay.manageusers";
    public const string InvoicesManage = "btcpay.store.vendorpay.manageinvoices";
    public const string InvoicesView = "btcpay.store.vendorpay.viewinvoices";
    public const string SettingsManage = "btcpay.store.vendorpay.managesettings";

    public static PolicyDefinition[] GetPolicyDefinitions()
    {
        return new[]
        {
            new PolicyDefinition(
                InvoicesView,
                new PermissionDisplay("Vendor Pay: View invoices",
                    "View and download Vendor Pay invoices."),
                new PermissionDisplay("Vendor Pay: View invoices",
                    "View and download Vendor Pay invoices on the selected stores.")),
            new PolicyDefinition(
                InvoicesManage,
                new PermissionDisplay("Vendor Pay: Manage invoices",
                    "Upload, pay, mark paid, delete, and otherwise action Vendor Pay invoices."),
                new PermissionDisplay("Vendor Pay: Manage invoices",
                    "Manage Vendor Pay invoices on the selected stores."),
                new[] { InvoicesView }),
            new PolicyDefinition(
                UsersManage,
                new PermissionDisplay("Vendor Pay: Manage users",
                    "Create, update, disable, reset, and remove Vendor Pay users."),
                new PermissionDisplay("Vendor Pay: Manage users",
                    "Manage Vendor Pay users on the selected stores.")),
            new PolicyDefinition(
                SettingsManage,
                new PermissionDisplay("Vendor Pay: Manage settings",
                    "View and update Vendor Pay settings and notification templates."),
                new PermissionDisplay("Vendor Pay: Manage settings",
                    "Manage Vendor Pay settings on the selected stores.")),
            new PolicyDefinition(
                Admin,
                new PermissionDisplay("Vendor Pay: Admin",
                    "Full administration of Vendor Pay users, invoices, and settings."),
                new PermissionDisplay("Vendor Pay: Admin",
                    "Full administration of Vendor Pay on the selected stores."),
                new[] { UsersManage, InvoicesManage, SettingsManage },
                new[] { Policies.CanModifyStoreSettings })
        };
    }
}
