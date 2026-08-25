namespace BTCPayServer.RockstarDev.Plugins.VendorPay.Logic;

/// <summary>
///     This class holds settings per store
/// </summary>
public class VendorPayStoreSetting
{
    public bool MakeInvoiceFilesOptional { get; set; }
    public bool PurchaseOrdersRequired { get; set; }
    public bool EmailOnInvoicePaid { get; set; }
    public string EmailOnInvoicePaidSubject { get; set; }
    public string EmailOnInvoicePaidBody { get; set; }
    public bool EmailReminders { get; set; }
    public string EmailRemindersSubject { get; set; }
    public string EmailRemindersBody { get; set; }
    public bool InvoiceFiatConversionAdjustment { get; set; }
    public double InvoiceFiatConversionAdjustmentPercentage { get; set; }

    // automatically set to be referenced in different places
    public string VendorPayPublicLink { get; set; }

    // Admin notifications on invoice upload
    public bool EmailAdminOnInvoiceUploaded { get; set; }
    public string EmailAdminOnInvoiceUploadedAddress { get; set; }
    public string EmailAdminOnInvoiceUploadedSubject { get; set; }
    public string EmailAdminOnInvoiceUploadedBody { get; set; }

    // Admin notifications on invoice deletion
    public bool EmailAdminOnInvoiceDeleted { get; set; }
    public string EmailAdminOnInvoiceDeletedAddress { get; set; }
    public string EmailAdminOnInvoiceDeletedSubject { get; set; }
    public string EmailAdminOnInvoiceDeletedBody { get; set; }

    // Uploader confirmation email on invoice upload
    public bool EmailUploaderOnInvoiceUploaded { get; set; }
    public string EmailUploaderOnInvoiceUploadedSubject { get; set; }
    public string EmailUploaderOnInvoiceUploadedBody { get; set; }

    // Accountless upload settings
    public bool AccountlessUploadEnabled { get; set; }
    public string UploadCode { get; set; }
    public string DescriptionTitle { get; set; }

    // Default User Invite email
    public string UserInviteEmailSubject { get; set; }
    public string UserInviteEmailBody { get; set; }

    // Reserved for a future paired-output payout revision. NOT wired at
    // runtime: no controller, hosted service, or view reads this flag today.
    // The prior implementation added contractor-supplied decoy addresses as
    // extra external recipients on the payout tx, which sent additional funds
    // to the vendor. Do not re-wire this setting to that shape - the correct
    // paired output must go to a sender-controlled change address, generated
    // fresh from the paying wallet at tx-build time. Restoring the feature
    // will land alongside batch shape preflight, a hardware-sign hint,
    // regtest coverage of the on-chain output shape, and a defeats /
    // does-not-defeat info box on the admin dashboard.
    public bool StonewallEnabled { get; set; }
}
