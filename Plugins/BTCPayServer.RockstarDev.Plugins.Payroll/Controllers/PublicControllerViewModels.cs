﻿using BTCPayServer.Models;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using static BTCPayServer.RockstarDev.Plugins.Payroll.Controllers.PayrollInvoiceController;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Controllers;

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
}

public class PublicChangePasswordViewModel : BasePayrollPublicViewModel
{
    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Current Password")]
    public string CurrentPassword { get; set; }
    [Required]
    [DataType(DataType.Password)]
    [MinLength(6)]
    [Display(Name = "New Password")]
    public string NewPassword { get; set; }
    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Confirm New Password")]
    [Compare("NewPassword", ErrorMessage = "Password fields don't match")]
    public string ConfirmNewPassword { get; set; }
}

public class BasePayrollPublicViewModel
{
    // store properties
    public string StoreId { get; set; }
    public string StoreName { get; set; }
    public StoreBrandingViewModel StoreBranding { get; set; }
}
