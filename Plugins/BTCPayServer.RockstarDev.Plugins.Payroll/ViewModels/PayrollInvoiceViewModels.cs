using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data.Models;
using BTCPayServer.RockstarDev.Plugins.Payroll.Logic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.ViewModels;

public class PayrollInvoiceListViewModel
{
    public bool All { get; set; }
    public List<PayrollInvoiceViewModel> PayrollInvoices { get; set; }
    public bool PurchaseOrdersRequired { get; set; }
}

public class PayrollInvoiceViewModel
{
    public DateTimeOffset CreatedAt { get; set; }
    public string Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
    public string Destination { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; }
    public PayrollInvoiceState State { get; set; }
    public string TxnId { get; set; }
    public string PurchaseOrder { get; set; }
    public string Description { get; set; }
    public string InvoiceUrl { get; set; }
    public string ExtraInvoiceFiles { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public string AdminNote { get; set; }
}

public class PayrollInvoiceUploadViewModel
{
    [Required]
    [DisplayName("User")]
    public string UserId { get; set; }
    public SelectList PayrollUsers { get; set; }
    
    [Required]
    public string Destination { get; set; }
    [Required]
    public decimal Amount { get; set; }
    [Required]
    public string Currency { get; set; }
        
    [RequiredIf("PurchaseOrdersRequired", true)]
    [DisplayName("Purchase Order")]
    [MaxLength(20)]
    public string PurchaseOrder { get; set; }

    public bool PurchaseOrdersRequired { get; set; }
    public string Description { get; set; }
    public IFormFile Invoice { get; set; }
    
    [DisplayName("Optional Extra Files (receipts, reimbursements, etc.)")]
    public List<IFormFile> ExtraFiles { get; set; } = new();

}