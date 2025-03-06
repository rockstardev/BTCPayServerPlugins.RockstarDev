using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.ViewModels;

public class PayrollSettingViewModel
{
    [Display(Name = "Make Invoice file optional")]
    public bool MakeInvoiceFileOptional { get; set; }
    
    [Display(Name = "Require Purchase Orders (PO)")]
    public bool PurchaseOrdersRequired { get; set; }

    [Display(Name = "Email vendor once invoice is paid")]
    public bool EmailOnInvoicePaid { get; set; }
    public string EmailOnInvoicePaidSubject { get; set; }
    public string EmailOnInvoicePaidBody { get; set; }

    [Display(Name = "Email reminders for vendors to upload invoices")]
    public bool EmailReminders { get; set; }
    public string EmailRemindersSubject { get; set; }
    public string EmailRemindersBody { get; set; }

    public record Defaults
    {
        public const string EmailOnInvoicePaidSubject = @"Your invoice has been paid";
        public const string EmailOnInvoicePaidBody = @"Hello {Name},

Your invoice submitted on {CreatedAt} has been paid on {PaidAt}.

See all your invoices on: {VendorPayPublicLink}

Thank you,
{StoreName}";
        
        public const string EmailRemindersSubject = @"Reminder to upload your invoice";
        public const string EmailRemindersBody = @"Hello {Name},

We're sending this email to remind you that it is time to upload your invoice.

Please proceed to: {VendorPayPublicLink}

Thank you,
{StoreName}";
    }
}