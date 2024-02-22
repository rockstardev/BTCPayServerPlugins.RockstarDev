using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data.Models;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NBitpayClient;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Controllers;

[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class PayrollUserController : Controller
{
    private readonly ApplicationDbContextFactory _dbContextFactory;
    private readonly PayrollPluginDbContextFactory _payrollPluginDbContextFactory;
    private readonly PayrollPluginPassHasher _hasher;

    public PayrollUserController(ApplicationDbContextFactory dbContextFactory,
        PayrollPluginDbContextFactory payrollPluginDbContextFactory,
        StoreRepository storeRepository,
        PayrollPluginPassHasher hasher)
    {
        _dbContextFactory = dbContextFactory;
        _payrollPluginDbContextFactory = payrollPluginDbContextFactory;
        _hasher = hasher;
    }
    public StoreData CurrentStore => HttpContext.GetStoreData();


    [HttpGet("~/plugins/{storeId}/payroll/users")]
    public async Task<IActionResult> List(string storeId)
    {
        var now = DateTimeOffset.UtcNow;
        await using var ctx = _payrollPluginDbContextFactory.CreateContext();
        var payrollUsers = await ctx.PayrollUsers
            .Where(a => a.StoreId == storeId)
            .OrderByDescending(data => data.Name).ToListAsync();

        return View(payrollUsers.ToList());
    }

    [HttpGet("~/plugins/{storeId}/payroll/users/create")]
    public async Task<IActionResult> Create()
    {
        return View(new PayrollUserCreateViewModel());
    }

    [HttpPost("~/plugins/{storeId}/payroll/users/create")]

    public async Task<IActionResult> Create(PayrollUserCreateViewModel model)
    {
        if (CurrentStore is null)
            return NotFound();

        await using var dbPlugins = _payrollPluginDbContextFactory.CreateContext();

        var userInDb = dbPlugins.PayrollUsers.SingleOrDefault(a =>
            a.StoreId == CurrentStore.Id && a.Email == model.Email.ToLowerInvariant());
        if (userInDb != null)
            ModelState.AddModelError(nameof(model.Email), "User with the same email already exists");

        if (!ModelState.IsValid)
            return View(model);

        var uid = Guid.NewGuid().ToString();

        var passHashed = _hasher.HashPassword(uid, model.Password);

        var dbUser = new PayrollUser
        {
            Id = uid,
            Name = model.Name,
            Email = model.Email.ToLowerInvariant(),
            Password = passHashed,
            StoreId = CurrentStore.Id
        };

        dbPlugins.Add(dbUser);
        await dbPlugins.SaveChangesAsync();

        return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
    }

    [HttpGet("~/plugins/payroll/users/edit/{userId}")]
    public async Task<IActionResult> Edit(string userId)
    {
        if (CurrentStore is null)
            return NotFound();

        await using var ctx = _payrollPluginDbContextFactory.CreateContext();

        PayrollUser user = ctx.PayrollUsers.SingleOrDefault(a => a.Id == userId && a.StoreId == CurrentStore.Id);
        PayrollUserCreateViewModel model = new PayrollUserCreateViewModel { Id = user.Id, Email = user.Email, Name = user.Name };
        return View(model);
    }

    [HttpPost("~/plugins/payroll/users/edit/{userId}")]
    public async Task<IActionResult> Edit(string userId, PayrollUserCreateViewModel model)
    {
        if (CurrentStore is null)
            return NotFound();

        await using var ctx = _payrollPluginDbContextFactory.CreateContext();

        PayrollUser user = ctx.PayrollUsers.SingleOrDefault(a => a.Id == userId && a.StoreId == CurrentStore.Id);

        user.Email = string.IsNullOrEmpty(model.Email) ? user.Email : model.Email;
        user.Name = string.IsNullOrEmpty(model.Name) ? user.Name : model.Name;

        ctx.Update(user);
        await ctx.SaveChangesAsync();

        ReturnMessageStatus();
        return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
    }


    [HttpGet("~/plugins/payroll/users/resetpassword/{userId}")]
    public async Task<IActionResult> ResetPassword(string userId)
    {
        if (CurrentStore is null)
            return NotFound();

        await using var ctx = _payrollPluginDbContextFactory.CreateContext();

        PayrollUser user = ctx.PayrollUsers.SingleOrDefault(a => a.Id == userId && a.StoreId == CurrentStore.Id);
        PayrollUserResetPasswordViewModel model = new PayrollUserResetPasswordViewModel { Id = user.Id };
        return View(model);
    }

    [HttpPost("~/plugins/payroll/users/resetpassword/{userId}")]
    public async Task<IActionResult> ResetPassword(string userId, PayrollUserResetPasswordViewModel model)
    {
        if (CurrentStore is null)
            return NotFound();

        await using var ctx = _payrollPluginDbContextFactory.CreateContext();

        PayrollUser user = ctx.PayrollUsers.SingleOrDefault(a => a.Id == userId && a.StoreId == CurrentStore.Id);

        if (!ModelState.IsValid)
            return View(model);

        var passHashed = _hasher.HashPassword(user.Id, model.NewPassword);
        user.Password = passHashed;

        ctx.Update(user);
        await ctx.SaveChangesAsync();

        ReturnMessageStatus();
        return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
    }

    private void ReturnMessageStatus()
    {
        TempData.SetStatusMessageModel(new StatusMessageModel()
        {
            Message = $"User details updated successfully",
            Severity = StatusMessageModel.StatusSeverity.Success
        });
    }

    [HttpGet("~/plugins/payroll/users/delete/{userId}")]
    public async Task<IActionResult> Delete(string userId)
    {
        if (CurrentStore is null)
            return NotFound();

        await using var ctx = _payrollPluginDbContextFactory.CreateContext();
        var payrollUser = ctx.PayrollUsers
            .SingleOrDefault(a => a.Id == userId && a.StoreId == CurrentStore.Id);

        var userHasInvoice = ctx.PayrollInvoices.Any(a =>
        a.UserId == payrollUser.Id);
        if (userHasInvoice)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Message = $"User can't be deleted since there are active invoices",
                Severity = StatusMessageModel.StatusSeverity.Error
            });
            return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
        }

        ctx.Remove(payrollUser);
        await ctx.SaveChangesAsync();

        TempData.SetStatusMessageModel(new StatusMessageModel()
        {
            Message = $"User deletion was successful",
            Severity = StatusMessageModel.StatusSeverity.Success
        });
        return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
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
}