namespace BTCPayServer.RockstarDev.Plugins.Payroll.Logic;

/// <summary>
///     This class holds settings per store
/// </summary>
public class PayrollStoreSetting
{
    public bool MakeInvoiceFilesOptional { get; set; }
    public bool PurchaseOrdersRequired { get; set; }
    public bool EmailOnInvoicePaid { get; set; }
    public string EmailOnInvoicePaidSubject { get; set; }
    public string EmailOnInvoicePaidBody { get; set; }
    public bool EmailReminders { get; set; }
    public string EmailRemindersSubject { get; set; }
    public string EmailRemindersBody { get; set; }
    public bool EnableInvoiceAdjustmentSpread { get; set; }
    public double InvoiceAdjustmentSpreadPercentage { get; set; }

    // automatically set to be referenced in different places
    public string VendorPayPublicLink { get; set; }
}
