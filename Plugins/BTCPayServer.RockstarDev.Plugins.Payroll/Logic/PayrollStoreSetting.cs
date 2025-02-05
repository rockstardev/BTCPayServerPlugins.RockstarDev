using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Logic;

/// <summary>
/// This class holds settings per store
/// </summary>
public class PayrollStoreSetting
{
    public bool MakeInvoiceFilesOptional { get; set; }
    public bool PurchaseOrdersRequired { get; set; }
    public bool EmailInviteForUsers { get; set; }
    public bool EmailOnInvoicePaid { get; set; }
    public string EmailOnInvoicePaidSubject { get; set; }
    public string EmailOnInvoicePaidBody { get; set; }
    
    // automatically set to be referenced in different places
    public string VendorPayPublicLink { get; set; }
}