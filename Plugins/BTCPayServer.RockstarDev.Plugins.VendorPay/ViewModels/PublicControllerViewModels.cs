﻿using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Models;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Logic;
using Microsoft.AspNetCore.Http;

namespace BTCPayServer.RockstarDev.Plugins.VendorPay.ViewModels;

public class PublicLoginViewModel : BaseVendorPayPublicViewModel
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
public class PublicListInvoicesViewModel : BaseVendorPayPublicViewModel
{
    public List<VendorPayInvoiceViewModel> Invoices { get; set; }
    public bool PurchaseOrdersRequired { get; set; }
}

public class PublicVendorPayInvoiceUploadViewModel : BaseVendorPayPublicViewModel
{
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

public class PublicChangePasswordViewModel : BaseVendorPayPublicViewModel
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

public class BaseVendorPayPublicViewModel
{
    // store properties
    public string StoreId { get; set; }
    public string StoreName { get; set; }
    public StoreBrandingViewModel StoreBranding { get; set; }
}
