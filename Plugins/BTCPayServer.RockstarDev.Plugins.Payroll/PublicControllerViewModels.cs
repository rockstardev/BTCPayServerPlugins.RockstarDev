using BTCPayServer.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static BTCPayServer.RockstarDev.Plugins.Payroll.PayrollInvoiceController;

namespace BTCPayServer.RockstarDev.Plugins.Payroll;

public class PublicLoginViewModel : BasePayrollPublicViewModel
{
    [Required]
    [EmailAddress]
    public string Email { get; set; }
    [Required]
    [DataType(DataType.Password)]
    public string Password { get; set; }
    [Display(Name = "Remember me")]
    public bool RememberMe { get; set; }
}
public class PublicListInvoicesViewModel : BasePayrollPublicViewModel
{
    public List<PayrollInvoiceViewModel> Invoices { get; set; }
}

public class PublicPayrollInvoiceUploadViewModel : BasePayrollPublicViewModel
{
    [Required]
    public string Destination { get; set; }
    [Required]
    public decimal Amount { get; set; }
    [Required]
    public string Currency { get; set; }
    public string Description { get; set; }
    [Required]
    public IFormFile Invoice { get; set; }



    public SelectList PayrollUsers { get; set; }
}

public class BasePayrollPublicViewModel
{
    // store properties
    public string StoreId { get; set; }
    public string StoreName { get; set; }
    public StoreBrandingViewModel StoreBranding { get; set; }
}
