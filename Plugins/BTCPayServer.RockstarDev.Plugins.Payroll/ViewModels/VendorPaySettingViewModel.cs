using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.RockstarDev.Plugins.VendorPay.ViewModels;

public class VendorPaySettingViewModel
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

    //
    [Display(Name = "Email confirmation to uploader")]
    public bool EmailUploaderOnInvoiceUploaded { get; set; }

    public string EmailUploaderOnInvoiceUploadedSubject { get; set; }
    public string EmailUploaderOnInvoiceUploadedBody { get; set; }

    //
    [Display(Name = "Enable accountless invoice upload")]
    public bool AccountlessUploadEnabled { get; set; }

    [Display(Name = "Upload code")]
    public string UploadCode { get; set; }

    [Display(Name = "Description title")]
    public string DescriptionTitle { get; set; }



    [Display(Name = "User Invite Email Subject")]
    public string UserInviteEmailSubject { get; set; }

    [Display(Name = "User Invite Email Template")]
    public string UserInviteEmailBody { get; set; }

    public record Defaults
    {
        public const string EmailOnInvoicePaidSubject = @"[VendorPay] Invoice paid";

        public const string EmailOnInvoicePaidBody = @"Hello {Name},

Your invoice submitted on {CreatedAt} has been paid on {PaidAt}.

View transaction on mempool: {MempoolAddress} 

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

        public const string EmailUploaderOnInvoiceUploadedSubject = @"[VendorPay] Invoice Upload Confirmation";

        public const string EmailUploaderOnInvoiceUploadedBody = @"Hello {VendorName},

Thank you for uploading your invoice.

Invoice ID: {InvoiceId}
Amount: {Amount} {Currency}
Destination: {Destination}

We will process your invoice and notify you once payment is complete.

Thank you,
{StoreName}";


        public const string UserInviteEmailSubject = "You are invited to create a Vendor Pay account";

        public const string UserInviteEmailBody = @"Hello {Name},

You are invited to create an account on {StoreName}'s Vendor Pay portal by visiting the following link:  
{VendorPayRegisterLink}

Once your account is created and you log in, you will be able to:
- View your invoices and submit new ones.
- Click 'Upload Invoice' to add a payable invoice. Fill out the information accurately. By using the Vendor Pay portal, you are solely responsible for providing an accurate Bitcoin address and assume all liability for any incorrect or inaccessible address.
- Describe what the payment is related to; be as descriptive as possible to avoid delays.
- Upload the corresponding invoice file.

Payments will be issued in accordance with the terms of the contracted payment and purchase order.

Thank you,  
{StoreName}";
    }
}
