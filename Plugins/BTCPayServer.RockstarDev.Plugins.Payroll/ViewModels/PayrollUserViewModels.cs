using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.ViewModels;

public class PayrollUserListViewModel
{
    public List<Data.Models.PayrollUser> PayrollUsers { get; set; }
    public bool All { get; set; }
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
    [Required]
    [MinLength(6)]
    [DataType(DataType.Password)]
    public string Password { get; set; }

    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Confirm Password")]
    [Compare("Password", ErrorMessage = "Password fields don't match")]
    public string ConfirmPassword { get; set; }

    [JsonIgnore]
    public string StoreId { get; set; }
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

