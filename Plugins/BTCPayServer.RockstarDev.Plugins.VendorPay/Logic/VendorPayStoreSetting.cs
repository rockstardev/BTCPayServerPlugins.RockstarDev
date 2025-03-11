using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.RockstarDev.Plugins.VendorPay.Logic;

/// <summary>
/// This class holds settings per store
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

    // automatically set to be referenced in different places
    public string VendorPayPublicLink { get; set; }
}