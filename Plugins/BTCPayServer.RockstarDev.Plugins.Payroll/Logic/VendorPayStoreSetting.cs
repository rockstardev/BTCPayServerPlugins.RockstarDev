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
}
