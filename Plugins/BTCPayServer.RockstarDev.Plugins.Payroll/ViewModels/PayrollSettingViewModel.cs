using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.ViewModels;

public class PayrollSettingViewModel
{
    [Display(Name = "Make Invoice file optional")]
    public bool MakeInvoiceFileOptional { get; set; }
    
    [Display(Name = "Require Purchase Orders (PO)")]
    public bool PurchaseOrdersRequired { get; set; }
}