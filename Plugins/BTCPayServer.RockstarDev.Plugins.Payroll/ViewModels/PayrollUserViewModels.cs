using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data.Models;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.ViewModels;

public class PayrollUserListViewModel
{
    public List<PayrollUser> PayrollUsers { get; set; }
    public bool All { get; set; }
    public bool Pending { get; set; }
}

public class PayrollUserCreateViewModel
{
    public string Id { get; set; }
    [MaxLength(50)]
    [Required]

    public string Name { get; set; }

    [Required]
    [EmailAddress]
    public string Email { get; set; }
    
    [MinLength(6)]
    [Display(Name = "Password (leave blank to generate invite-link)")]
    [DataType(DataType.Password)]
    public string Password { get; set; }

    [DataType(DataType.Password)]
    [Display(Name = "Confirm Password")]
    [Compare("Password", ErrorMessage = "Password fields don't match")]
    public string ConfirmPassword { get; set; }
}

public class PayrollUserResetPasswordViewModel
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

public class AcceptInvitationRequestViewModel : BasePayrollPublicViewModel
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
    public string Store { get; set; }
    public string RegistrationLink { get; set; }
    public string UserName { get; set; }
}