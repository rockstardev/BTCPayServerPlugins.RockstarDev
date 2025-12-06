using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.ViewModels;

public class PayrollSettingViewModel
{
    [Display(Name = "Make Invoice file optional")]
    public bool MakeInvoiceFileOptional { get; set; }

    [Display(Name = "Require Purchase Orders (PO)")]
    public bool PurchaseOrdersRequired { get; set; }

    [Display(Name = "Invoice fiat conversion adjustment")]
    public bool InvoiceFiatConversionAdjustment { get; set; }
    [Display(Name = "Invoice fiat conversion adjustment percentage")]
    public double InvoiceFiatConversionAdjustmentPercentage { get; set; }

    //
    [Display(Name = "Email vendor once invoice is paid")]
    public bool EmailOnInvoicePaid { get; set; }

    public string EmailOnInvoicePaidSubject { get; set; }
    public string EmailOnInvoicePaidBody { get; set; }

    //
    [Display(Name = "Email reminders for vendors to upload invoices")]
    public bool EmailReminders { get; set; }

    public string EmailRemindersSubject { get; set; }
    public string EmailRemindersBody { get; set; }
    
    
    //
    [Display(Name = "Email admin when invoice is uploaded")]
    public bool EmailAdminOnInvoiceUploaded { get; set; }

    [Display(Name = "Admin email addresses")]
    public string EmailAdminOnInvoiceUploadedAddress { get; set; }

    public string EmailAdminOnInvoiceUploadedSubject { get; set; }
    public string EmailAdminOnInvoiceUploadedBody { get; set; }

    //
    [Display(Name = "Email admin when invoice is deleted")]
    public bool EmailAdminOnInvoiceDeleted { get; set; }

    [Display(Name = "Admin email addresses")]
    public string EmailAdminOnInvoiceDeletedAddress { get; set; }

    public string EmailAdminOnInvoiceDeletedSubject { get; set; }
    public string EmailAdminOnInvoiceDeletedBody { get; set; }

    public record Defaults
    {
        public const string EmailOnInvoicePaidSubject = @"[VendorPay] Invoice paid";

        public const string EmailOnInvoicePaidBody = @"Hello {Name},

Your invoice submitted on {CreatedAt} has been paid on {PaidAt}.

See all your invoices on: {VendorPayPublicLink}

Thank you,
{StoreName}";

        public const string EmailRemindersSubject = @"[VendorPay] Reminder to upload invoice";

        public const string EmailRemindersBody = @"Hello {Name},

We're sending this email to remind you that it is time to upload your invoice.

Please proceed to: {VendorPayPublicLink}

Thank you,
{StoreName}";

        public const string EmailAdminOnInvoiceUploadedSubject = @"[VendorPay] Invoice Uploaded";

        public const string EmailAdminOnInvoiceUploadedBody = @"Hello,

An invoice has been uploaded by {VendorName} <{VendorEmail}>.

Invoice ID: {InvoiceId}
Amount: {Amount} {Currency}
Destination: {Destination}

Thank you.";

        public const string EmailAdminOnInvoiceDeletedSubject = @"[VendorPay] Invoice Deleted";

        public const string EmailAdminOnInvoiceDeletedBody = @"Hello,

An invoice has been deleted by {VendorName} <{VendorEmail}>.

Invoice ID: {InvoiceId}
Amount: {Amount} {Currency}
Destination: {Destination}

Thank you.";
    }
}
