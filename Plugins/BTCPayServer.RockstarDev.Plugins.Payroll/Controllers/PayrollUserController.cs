using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data.Models;
using BTCPayServer.RockstarDev.Plugins.Payroll.Services;
using BTCPayServer.RockstarDev.Plugins.Payroll.Services.Helpers;
using BTCPayServer.RockstarDev.Plugins.Payroll.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Controllers;

[Route("~/plugins/{storeId}/vendorpay/users/", Order = 0)]
[Route("~/plugins/{storeId}/payroll/users/", Order = 1)]
[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class PayrollUserController(
    PluginDbContextFactory pluginDbContextFactory,
    VendorPayPassHasher hasher,
    EmailService emailService,
    InvoicesDownloadHelper invoicesDownloadHelper)
    : Controller
{
    private const string UserInviteEmailSubject = "You are invited to create a Vendor Pay account";

    private const string UserInviteEmailBody = @"Hello {Name},

You are invited to create an account on {StoreName}'s Vendor Pay portal by visiting the following link:  
{VendorPayRegisterLink}

Once your account is created and you log in, you will be able to:
- View your invoices and submit new ones.
- Click 'Upload Invoice' to add a payable invoice. Fill out the information accurately. By using the Vendor Pay portal, you are solely responsible for providing an accurate Bitcoin address and assume all liability for any incorrect or inaccessible address.
- Describe what the payment is related to; be as descriptive as possible to avoid delays.
- Upload the corresponding invoice file.

Payments will be issued in accordance with the terms of the contracted payment and purchase order.

Thank you,  
{StoreName}";

    public StoreData CurrentStore => HttpContext.GetStoreData();


    [HttpGet("list")]
    public async Task<IActionResult> List(string storeId, bool all, bool pending)
    {
        await using var ctx = pluginDbContextFactory.CreateContext();
        var query = ctx.PayrollUsers.Where(a => a.StoreId == storeId);
        if (pending)
            query = query.Where(a => a.State == PayrollUserState.Pending);
        else if (!all) query = query.Where(a => a.State == PayrollUserState.Active);
        var payrollUsers = query.OrderByDescending(data => data.Name).ToList();
        var payrollUserListViewModel = new PayrollUserListViewModel
        {
            All = all,
            Pending = pending,
            DisplayedPayrollUsers = payrollUsers,
            AllPayrollUsers = ctx.PayrollUsers.Where(a => a.StoreId == storeId).ToList()
        };
        return View(payrollUserListViewModel);
    }

    [HttpGet("create")]
    public async Task<IActionResult> Create()
    {
        if (CurrentStore is null)
            return NotFound();

        var isEmailSettingsConfigured = await emailService.IsEmailSettingsConfigured(CurrentStore.Id);
        var vm = new PayrollUserCreateViewModel
        {
            StoreId = CurrentStore.Id,
            UserInviteEmailBody = UserInviteEmailBody,
            UserInviteEmailSubject = UserInviteEmailSubject,
            StoreEmailSettingsConfigured = isEmailSettingsConfigured
        };
        return View(vm);
    }

    [HttpPost("create")]
    public async Task<IActionResult> Create(PayrollUserCreateViewModel model)
    {
        if (CurrentStore is null)
            return NotFound();

        await using var dbPlugins = pluginDbContextFactory.CreateContext();

        var email = model.Email.ToLowerInvariant();
        model.StoreEmailSettingsConfigured = await emailService.IsEmailSettingsConfigured(CurrentStore.Id);
        if (await dbPlugins.PayrollUsers.AnyAsync(a => a.StoreId == CurrentStore.Id && a.Email == email))
        {
            ModelState.AddModelError(nameof(model.Email), "User with the same email already exists");
            return View(model);
        }

        var uid = Guid.NewGuid().ToString();
        var dbUser = new PayrollUser
        {
            Id = uid,
            Name = model.Name,
            Email = email,
            StoreId = CurrentStore.Id,
            State = model.SendRegistrationEmailInviteToUser ? PayrollUserState.Pending : PayrollUserState.Active
        };
        if (model.SendRegistrationEmailInviteToUser)
        {
            var existingInvitation = await dbPlugins.PayrollInvitations
                .SingleOrDefaultAsync(i => i.Email == email && i.StoreId == CurrentStore.Id && !i.AcceptedAt.HasValue);
            if (existingInvitation != null)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Message = "An invitation has already been sent to this user", Severity = StatusMessageModel.StatusSeverity.Warning
                });
                return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
            }

            var invitation = new PayrollInvitation
            {
                Id = uid,
                StoreId = CurrentStore.Id,
                Email = email,
                Name = model.Name,
                Token = GenerateUniqueToken(),
                CreatedAt = DateTime.UtcNow
            };
            try
            {
                await emailService.SendUserInvitationEmail(dbUser, model.UserInviteEmailSubject, model.UserInviteEmailBody,
                    Url.Action("AcceptInvitation", "Public", new { storeId = CurrentStore.Id, invitation.Token }, Request.Scheme));
            }
            catch (Exception)
            {
                ModelState.AddModelError(nameof(model.Email),
                    "Invitation email sending failed, kindly check your SMTP settings and ensure they are correct");
                return View(model);
            }

            dbPlugins.Add(dbUser);
            dbPlugins.Add(invitation);
            await dbPlugins.SaveChangesAsync();
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = "Invitation email sent to " + model.Email, Severity = StatusMessageModel.StatusSeverity.Success
            });
        }
        else
        {
            if (string.IsNullOrWhiteSpace(model.Password))
            {
                ModelState.AddModelError(nameof(model.Password), "Password cannot be empty");
                return View(model);
            }

            dbUser.Password = hasher.HashPassword(uid, model.Password);
            dbPlugins.Add(dbUser);
            await dbPlugins.SaveChangesAsync();
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = "User created successfully", Severity = StatusMessageModel.StatusSeverity.Success
            });
        }

        return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
    }

    [HttpGet("resend-invitation/{userId}")]
    public async Task<IActionResult> ResendInvitation(string storeId, string userId)
    {
        if (CurrentStore is null)
            return NotFound();

        await using var ctx = pluginDbContextFactory.CreateContext();
        var user = ctx.PayrollUsers.SingleOrDefault(a => a.Id == userId && a.StoreId == CurrentStore.Id && a.State == PayrollUserState.Pending);
        if (user == null)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = "Cannot send reminder to user. Invalid user state specified", Severity = StatusMessageModel.StatusSeverity.Error
            });
            return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
        }

        var existingInvitation = ctx.PayrollInvitations.FirstOrDefault(i => i.Email == user.Email && i.StoreId == CurrentStore.Id);
        existingInvitation.Token = GenerateUniqueToken();
        existingInvitation.CreatedAt = DateTime.UtcNow;
        try
        {
            await emailService.SendUserInvitationEmail(user, UserInviteEmailSubject, UserInviteEmailBody,
                Url.Action("AcceptInvitation", "Public", new { storeId = CurrentStore.Id, existingInvitation.Token }, Request.Scheme));
        }
        catch (Exception)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = "Invitation email resending failed, kindly check your SMTP settings and ensure they are correct",
                Severity = StatusMessageModel.StatusSeverity.Error
            });
            return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
        }

        ctx.Update(existingInvitation);
        await ctx.SaveChangesAsync();
        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Message = "User invitation successfully sent", Severity = StatusMessageModel.StatusSeverity.Success
        });
        return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id, pending = true });
    }


    [HttpGet("edit/{userId}")]
    public async Task<IActionResult> Edit(string userId)
    {
        if (CurrentStore is null)
            return NotFound();

        await using var ctx = pluginDbContextFactory.CreateContext();

        var user = ctx.PayrollUsers.SingleOrDefault(a => a.Id == userId && a.StoreId == CurrentStore.Id);
        if (user.State == PayrollUserState.Pending)
            return NotFound();

        var model = new PayrollUserCreateViewModel
        {
            Id = user.Id,
            Email = user.Email,
            Name = user.Name,
            EmailReminder = user.EmailReminder,
            StoreEmailSettingsConfigured = await emailService.IsEmailSettingsConfigured(CurrentStore.Id)
        };
        return View(model);
    }

    [HttpPost("edit/{userId}")]
    public async Task<IActionResult> Edit(string userId, PayrollUserCreateViewModel model)
    {
        if (CurrentStore is null)
            return NotFound();

        if (!ModelState.IsValid)
        {
            model.StoreEmailSettingsConfigured = await emailService.IsEmailSettingsConfigured(CurrentStore.Id);
            return View(model);
        }

        await using var ctx = pluginDbContextFactory.CreateContext();

        var user = ctx.PayrollUsers.SingleOrDefault(a => a.Id == userId && a.StoreId == CurrentStore.Id);

        user.Email = string.IsNullOrEmpty(model.Email) ? user.Email : model.Email;
        user.Name = string.IsNullOrEmpty(model.Name) ? user.Name : model.Name;

        user.EmailReminder = string.IsNullOrEmpty(model.EmailReminder)
            ? user.EmailReminder
            : string.Join(",", model.EmailReminder.Split(',')
                .Select(r => r.Trim()).Where(r => !string.IsNullOrEmpty(r))
                .Distinct().OrderBy(int.Parse));

        ctx.Update(user);
        await ctx.SaveChangesAsync();

        ReturnMessageStatus();
        return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
    }


    [HttpGet("resetpassword/{userId}")]
    public async Task<IActionResult> ResetPassword(string userId)
    {
        if (CurrentStore is null)
            return NotFound();

        await using var ctx = pluginDbContextFactory.CreateContext();

        var user = ctx.PayrollUsers.SingleOrDefault(a => a.Id == userId && a.StoreId == CurrentStore.Id);
        if (user.State == PayrollUserState.Pending)
            return NotFound();
        var model = new PayrollUserResetPasswordViewModel { Id = user.Id };
        return View(model);
    }

    [HttpPost("resetpassword/{userId}")]
    public async Task<IActionResult> ResetPassword(string userId, PayrollUserResetPasswordViewModel model)
    {
        if (CurrentStore is null)
            return NotFound();

        await using var ctx = pluginDbContextFactory.CreateContext();

        var user = ctx.PayrollUsers.SingleOrDefault(a => a.Id == userId && a.StoreId == CurrentStore.Id);

        if (!ModelState.IsValid)
            return View(model);

        var passHashed = hasher.HashPassword(user.Id, model.NewPassword);
        user.Password = passHashed;

        ctx.Update(user);
        await ctx.SaveChangesAsync();

        ReturnMessageStatus();
        return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
    }

    [HttpGet("toggle/{userId}")]
    public async Task<IActionResult> ToggleUserStatus(string userId, bool enable)
    {
        if (CurrentStore is null)
            return NotFound();

        await using var ctx = pluginDbContextFactory.CreateContext();
        var user = ctx.PayrollUsers.SingleOrDefault(a => a.Id == userId && a.StoreId == CurrentStore.Id);

        if (user == null)
            return NotFound();

        return View("Confirm",
            new ConfirmModel($"{(enable ? "Activate" : "Disable")} user",
                $"The user ({user.Name}) will be {(enable ? "activated" : "disabled")}. Are you sure?", enable ? "Activate" : "Disable"));
    }


    [HttpPost("toggle/{userId}")]
    public async Task<IActionResult> ToggleUserStatusPost(string userId, bool enable)
    {
        if (CurrentStore is null)
            return NotFound();

        await using var ctx = pluginDbContextFactory.CreateContext();
        var user = ctx.PayrollUsers.SingleOrDefault(a => a.Id == userId && a.StoreId == CurrentStore.Id);

        if (user == null)
            return NotFound();

        switch (user.State)
        {
            case PayrollUserState.Disabled:
                user.State = PayrollUserState.Active;
                break;
            case PayrollUserState.Active:
                user.State = PayrollUserState.Disabled;
                break;
            case PayrollUserState.Archived:
                // Would need to know use case for this.
                break;
        }

        await ctx.SaveChangesAsync();

        TempData[WellKnownTempData.SuccessMessage] = $"User {(enable ? "activated" : "disabled")} successfully";

        return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
    }

    [HttpGet("downloadinvoices/{userId}")]
    public async Task<IActionResult> DownloadInvoices(string userId)
    {
        if (CurrentStore is null)
            return NotFound();

        await using var ctx = pluginDbContextFactory.CreateContext();
        var user = ctx.PayrollUsers.Single(a => a.Id == userId && a.StoreId == CurrentStore.Id);

        var payrollInvoices = await ctx.PayrollInvoices
            .Include(c => c.User)
            .Where(p => p.UserId == user.Id)
            .OrderByDescending(data => data.CreatedAt).ToListAsync();
        if (!payrollInvoices.Any())
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = "No payroll invoice available for download", Severity = StatusMessageModel.StatusSeverity.Info
            });
            return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
        }

        return await invoicesDownloadHelper.Process(payrollInvoices, HttpContext.Request.GetAbsoluteRootUri());
    }


    private void ReturnMessageStatus()
    {
        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Message = "User details updated successfully", Severity = StatusMessageModel.StatusSeverity.Success
        });
    }

    [HttpGet("delete/{userId}")]
    public async Task<IActionResult> Delete(string userId)
    {
        if (CurrentStore is null)
            return NotFound();

        await using var ctx = pluginDbContextFactory.CreateContext();
        var user = ctx.PayrollUsers.SingleOrDefault(a => a.Id == userId && a.StoreId == CurrentStore.Id);

        if (user == null)
            return NotFound();

        if (ctx.PayrollInvoices.Any(a => a.UserId == user.Id &&
                                         a.State != PayrollInvoiceState.Completed && a.State != PayrollInvoiceState.Cancelled))
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = "User can't be deleted since there are active invoices for this user", Severity = StatusMessageModel.StatusSeverity.Error
            });
            return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
        }

        var completedOrCancelledInvoices = ctx.PayrollInvoices.Count(a =>
            a.UserId == user.Id && (a.State == PayrollInvoiceState.Completed || a.State == PayrollInvoiceState.Cancelled));

        var invoiceDeleteText = completedOrCancelledInvoices > 0
            ? $"The user: {user.Name} will be deleted along with {completedOrCancelledInvoices} associated invoices. Are you sure you want to proceed?"
            : $"The user: {user.Name} will be deleted. Are you sure?";

        return View("Confirm", new ConfirmModel("Delete user", invoiceDeleteText, "Delete"));
    }


    [HttpPost("delete/{userId}")]
    public async Task<IActionResult> DeletePost(string userId)
    {
        if (CurrentStore is null)
            return NotFound();

        await using var ctx = pluginDbContextFactory.CreateContext();
        var payrollUser = ctx.PayrollUsers
            .Single(a => a.Id == userId && a.StoreId == CurrentStore.Id);

        var userInvoices = ctx.PayrollInvoices.Where(a => a.UserId == payrollUser.Id).ToList();
        if (userInvoices.Any(a => a.State != PayrollInvoiceState.Completed && a.State != PayrollInvoiceState.Cancelled))
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = "User can't be deleted since there are active invoices for this user", Severity = StatusMessageModel.StatusSeverity.Error
            });
            return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
        }

        var payrollUserInvite = ctx.PayrollInvitations.Where(c => c.Email == payrollUser.Email && c.StoreId == payrollUser.StoreId).ToList();
        if (payrollUserInvite.Any()) ctx.RemoveRange(payrollUserInvite);
        ctx.RemoveRange(userInvoices);
        ctx.Remove(payrollUser);
        await ctx.SaveChangesAsync();

        TempData.SetStatusMessageModel(new StatusMessageModel { Message = "User deleted successfully", Severity = StatusMessageModel.StatusSeverity.Success });
        return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
    }

    public string GenerateUniqueToken()
    {
        var tokenData = new byte[32];
        RandomNumberGenerator.Fill(tokenData);
        return Convert.ToBase64String(tokenData)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }
}
