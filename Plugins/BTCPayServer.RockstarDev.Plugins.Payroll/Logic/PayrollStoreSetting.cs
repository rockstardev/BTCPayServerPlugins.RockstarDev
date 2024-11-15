namespace BTCPayServer.RockstarDev.Plugins.Payroll.Logic;

/// <summary>
/// This class holds settings per store
/// </summary>
public class PayrollStoreSetting
{
    public bool MakeInvoiceFilesOptional { get; set; }
    public bool PurchaseOrdersRequired { get; set; }
}