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
}