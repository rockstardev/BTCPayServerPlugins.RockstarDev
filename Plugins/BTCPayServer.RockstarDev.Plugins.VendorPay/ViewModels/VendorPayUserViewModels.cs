using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Data.Models;

namespace BTCPayServer.RockstarDev.Plugins.VendorPay.ViewModels;

public class VendorPayUserListViewModel
{
    public List<VendorPayUser> DisplayedVendorPayUsers { get; set; }
    public List<VendorPayUser> AllVendorPayUsers { get; set; }
    public bool Pending { get; set; }
    public bool All { get; set; }
}

public class VendorPayUserCreateViewModel
{
    public string Id { get; set; }

    [MaxLength(50)]
    [Required]
    public string Name { get; set; }

    [Required]
    [EmailAddress]
    public string Email { get; set; }

    [Display(Name = "Invite user over email to create a password")]
    public bool SendRegistrationEmailInviteToUser { get; set; }

    [Display(Name = "Email Subject")]
    public string UserInviteEmailSubject { get; set; }

    [Display(Name = "Email Body")]
    public string UserInviteEmailBody { get; set; }

    [MinLength(6)]
    [DataType(DataType.Password)]
    public string Password { get; set; }

    [DataType(DataType.Password)]
    [Display(Name = "Confirm Password")]
    [Compare("Password", ErrorMessage = "Password fields don't match")]
    public string ConfirmPassword { get; set; }

    [Display(Name = "Email Reminder Days")]
    public string EmailReminder { get; set; }
    [JsonIgnore]
    public string StoreId { get; set; }

    public bool StoreEmailSettingsConfigured { get; set; }
}

public class VendorPayUserResetPasswordViewModel
{
    public string Id { get; set; }
    [Required]
    [MinLength(6)]
    [DataType(DataType.Password)]
    [Display(Name = "New Password")]
    public string NewPassword { get; set; }

    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Confirm New Password")]
    [Compare("NewPassword", ErrorMessage = "Password fields don't match")]
    public string ConfirmNewPassword { get; set; }
}

public class AcceptInvitationRequestViewModel : BaseVendorPayPublicViewModel
{
    public string Token { get; set; }
    public string Email { get; set; }
    public string Name { get; set; }
    [Required]
    [MinLength(6)]
    [DataType(DataType.Password)]
    [Display(Name = "New Password")]
    public string NewPassword { get; set; }

    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Confirm New Password")]
    [Compare("NewPassword", ErrorMessage = "Password fields don't match")]
    public string ConfirmNewPassword { get; set; }
}

public class InvitationEmailModel
{
    public string StoreId { get; set; }
    public string StoreName { get; set; }
    public string VendorPayRegisterLink { get; set; }
    public string UserName { get; set; }
    public string UserEmail { get; set; }
    public string Subject { get; set; }

}
