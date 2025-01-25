namespace BTCPayServer.RockstarDev.Plugins.Payroll.Logic;

/// <summary>
/// This class holds settings per store
/// </summary>
public class PayrollStoreSetting
{
    public bool MakeInvoiceFilesOptional { get; set; }
    public bool PurchaseOrdersRequired { get; set; }
    public bool EmailVendorOnInvoicePaid { get; set; }
    public string EmailTemplate { get; set; }
    
    // automatically set to be referenced in different places
    public string VendorPayPublicLink { get; set; }
}