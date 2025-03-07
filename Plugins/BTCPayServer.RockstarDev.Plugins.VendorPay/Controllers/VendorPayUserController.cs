﻿using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Security.Cryptography;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Data;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Services;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Services.Helpers;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Data.Models;
using BTCPayServer.RockstarDev.Plugins.VendorPay.ViewModels;

namespace BTCPayServer.RockstarDev.Plugins.VendorPay.Controllers;

[Route("~/plugins/{storeId}/vendorpay/users/", Order = 0)]
[Route("~/plugins/{storeId}/payroll/users/", Order = 1)]
[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class VendorPayUserController(
    VendorPayPluginDbContextFactory vendorpayPluginDbContextFactory,
    VendorPayPassHasher hasher,
    EmailService emailService,
    InvoicesDownloadHelper invoicesDownloadHelper)
    : Controller
{
    public StoreData CurrentStore => HttpContext.GetStoreData();
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


    [HttpGet("list")]
    public async Task<IActionResult> List(string storeId, bool all, bool pending)
    {
        await using var ctx = vendorpayPluginDbContextFactory.CreateContext();
        IQueryable<VendorPayUser> query = ctx.PayrollUsers.Where(a => a.StoreId == storeId);
        if (pending)
        {
            query = query.Where(a => a.State == VendorPayUserState.Pending);
        }
        else if (!all)
        {
            query = query.Where(a => a.State == VendorPayUserState.Active);
        }
        List<VendorPayUser> vendorpayUsers = query.OrderByDescending(data => data.Name).ToList();
        var vendorpayUserListViewModel = new VendorPayUserListViewModel
        {
            All = all,
            Pending = pending,
            DisplayedVendorPayUsers = vendorpayUsers,
            AllVendorPayUsers = ctx.PayrollUsers.Where(a => a.StoreId == storeId).ToList()
        };
        return View(vendorpayUserListViewModel);
    }

    [HttpGet("create")]
    public async Task<IActionResult> Create()
    {
        if (CurrentStore is null)
            return NotFound();

        var isEmailSettingsConfigured = await emailService.IsEmailSettingsConfigured(CurrentStore.Id);
        var vm = new VendorPayUserCreateViewModel
        { 
            StoreId = CurrentStore.Id, 
            UserInviteEmailBody = UserInviteEmailBody, 
            UserInviteEmailSubject = UserInviteEmailSubject,
            StoreEmailSettingsConfigured = isEmailSettingsConfigured
        };
        return View(vm);
    }

    [HttpPost("create")]
    public async Task<IActionResult> Create(VendorPayUserCreateViewModel model)
    {
        if (CurrentStore is null)
            return NotFound();

        await using var dbPlugins = vendorpayPluginDbContextFactory.CreateContext();

        var email = model.Email.ToLowerInvariant();
        model.StoreEmailSettingsConfigured = await emailService.IsEmailSettingsConfigured(CurrentStore.Id);
        if (await dbPlugins.PayrollUsers.AnyAsync(a => a.StoreId == CurrentStore.Id && a.Email == email))
        {
            ModelState.AddModelError(nameof(model.Email), "User with the same email already exists");
            return View(model);
        }
        var uid = Guid.NewGuid().ToString();
        var dbUser = new VendorPayUser
        {
            Id = uid,
            Name = model.Name,
            Email = email,
            StoreId = CurrentStore.Id,
            State = model.SendRegistrationEmailInviteToUser ? VendorPayUserState.Pending : VendorPayUserState.Active
        };
        if (model.SendRegistrationEmailInviteToUser)
        {
            var existingInvitation = await dbPlugins.PayrollInvitations
                .SingleOrDefaultAsync(i => i.Email == email && i.StoreId == CurrentStore.Id && !i.AcceptedAt.HasValue);
            if (existingInvitation != null)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Message = "An invitation has already been sent to this user",
                    Severity = StatusMessageModel.StatusSeverity.Warning
                });
                return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
            }
            var invitation = new VendorPayInvitation
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
                Message = "Invitation email sent to "+ model.Email,
                Severity = StatusMessageModel.StatusSeverity.Success
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
                Message = "User created successfully",
                Severity = StatusMessageModel.StatusSeverity.Success
            });
        }
        return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
    }

    [HttpGet("resend-invitation/{userId}")]
    public async Task<IActionResult> ResendInvitation(string storeId, string userId)
    {
        if (CurrentStore is null)
            return NotFound();

        await using var ctx = vendorpayPluginDbContextFactory.CreateContext();
        VendorPayUser user = ctx.PayrollUsers.SingleOrDefault(a => a.Id == userId && a.StoreId == CurrentStore.Id && a.State == VendorPayUserState.Pending);
        if (user == null)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Message = $"Cannot send reminder to user. Invalid user state specified",
                Severity = StatusMessageModel.StatusSeverity.Error
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
            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Message = $"Invitation email resending failed, kindly check your SMTP settings and ensure they are correct",
                Severity = StatusMessageModel.StatusSeverity.Error
            });
            return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
        }
        ctx.Update(existingInvitation);
        await ctx.SaveChangesAsync();
        TempData.SetStatusMessageModel(new StatusMessageModel()
        {
            Message = $"User invitation successfully sent",
            Severity = StatusMessageModel.StatusSeverity.Success
        });
        return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id, pending = true });
    }


    [HttpGet("edit/{userId}")]
    public async Task<IActionResult> Edit(string userId)
    {
        if (CurrentStore is null)
            return NotFound();

        await using var ctx = vendorpayPluginDbContextFactory.CreateContext();

        var user = ctx.PayrollUsers.SingleOrDefault(a => a.Id == userId && a.StoreId == CurrentStore.Id);
        if (user.State == VendorPayUserState.Pending)
            return NotFound();

        var model = new VendorPayUserCreateViewModel
        {
            Id = user.Id, Email = user.Email, Name = user.Name, EmailReminder = user.EmailReminder, 
            StoreEmailSettingsConfigured = await emailService.IsEmailSettingsConfigured(CurrentStore.Id)
        };
        return View(model);
    }

    [HttpPost("edit/{userId}")]
    public async Task<IActionResult> Edit(string userId, VendorPayUserCreateViewModel model)
    {
        if (CurrentStore is null)
            return NotFound();

        if (!ModelState.IsValid)
        {
            model.StoreEmailSettingsConfigured = await emailService.IsEmailSettingsConfigured(CurrentStore.Id);
            return View(model);
        }

        await using var ctx = vendorpayPluginDbContextFactory.CreateContext();

        VendorPayUser user = ctx.PayrollUsers.SingleOrDefault(a => a.Id == userId && a.StoreId == CurrentStore.Id);

        user.Email = string.IsNullOrEmpty(model.Email) ? user.Email : model.Email;
        user.Name = string.IsNullOrEmpty(model.Name) ? user.Name : model.Name;

        user.EmailReminder = string.IsNullOrEmpty(model.EmailReminder) ? user.EmailReminder : 
            string.Join(",", model.EmailReminder.Split(',')
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

        await using var ctx = vendorpayPluginDbContextFactory.CreateContext();

        VendorPayUser user = ctx.PayrollUsers.SingleOrDefault(a => a.Id == userId && a.StoreId == CurrentStore.Id);
        if (user.State == VendorPayUserState.Pending)
            return NotFound();
        VendorPayUserResetPasswordViewModel model = new VendorPayUserResetPasswordViewModel { Id = user.Id };
        return View(model);
    }

    [HttpPost("resetpassword/{userId}")]
    public async Task<IActionResult> ResetPassword(string userId, VendorPayUserResetPasswordViewModel model)
    {
        if (CurrentStore is null)
            return NotFound();

        await using var ctx = vendorpayPluginDbContextFactory.CreateContext();

        VendorPayUser user = ctx.PayrollUsers.SingleOrDefault(a => a.Id == userId && a.StoreId == CurrentStore.Id);

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

        await using var ctx = vendorpayPluginDbContextFactory.CreateContext();
        VendorPayUser user = ctx.PayrollUsers.SingleOrDefault(a => a.Id == userId && a.StoreId == CurrentStore.Id);

        if (user == null)
            return NotFound();

        return View("Confirm", new ConfirmModel($"{(enable ? "Activate" : "Disable")} user", $"The user ({user.Name}) will be {(enable ? "activated" : "disabled")}. Are you sure?", (enable ? "Activate" : "Disable")));
    }


    [HttpPost("toggle/{userId}")]
    public async Task<IActionResult> ToggleUserStatusPost(string userId, bool enable)
    {
        if (CurrentStore is null)
            return NotFound();

        await using var ctx = vendorpayPluginDbContextFactory.CreateContext();
        VendorPayUser user = ctx.PayrollUsers.SingleOrDefault(a => a.Id == userId && a.StoreId == CurrentStore.Id);

        if (user == null)
            return NotFound();

        switch (user.State)
        {
            case VendorPayUserState.Disabled:
                user.State = VendorPayUserState.Active;
                break;
            case VendorPayUserState.Active:
                user.State = VendorPayUserState.Disabled;
                break;
            case VendorPayUserState.Archived:
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

        await using var ctx = vendorpayPluginDbContextFactory.CreateContext();
        var user = ctx.PayrollUsers.Single(a => a.Id == userId && a.StoreId == CurrentStore.Id);

        var vendorpayInvoices = await ctx.PayrollInvoices
            .Include(c => c.User)
            .Where(p => p.UserId == user.Id)
            .OrderByDescending(data => data.CreatedAt).ToListAsync();
        if (!vendorpayInvoices.Any())
        {
            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Message = $"No vendorpay invoice available for download",
                Severity = StatusMessageModel.StatusSeverity.Info
            });
            return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
        }

        return await invoicesDownloadHelper.Process(vendorpayInvoices, HttpContext.Request.GetAbsoluteRootUri());
    }


    private void ReturnMessageStatus()
    {
        TempData.SetStatusMessageModel(new StatusMessageModel()
        {
            Message = $"User details updated successfully",
            Severity = StatusMessageModel.StatusSeverity.Success
        });
    }

    [HttpGet("delete/{userId}")]
    public async Task<IActionResult> Delete(string userId)
    {
        if (CurrentStore is null)
            return NotFound();

        await using var ctx = vendorpayPluginDbContextFactory.CreateContext();
        var user = ctx.PayrollUsers.SingleOrDefault(a => a.Id == userId && a.StoreId == CurrentStore.Id);

        if (user == null)
            return NotFound();

        if (ctx.PayrollInvoices.Any(a => a.UserId == user.Id &&
            (a.State != VendorPayInvoiceState.Completed && a.State != VendorPayInvoiceState.Cancelled)))
        {
            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Message = $"User can't be deleted since there are active invoices for this user",
                Severity = StatusMessageModel.StatusSeverity.Error
            });
            return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
        }
        var completedOrCancelledInvoices = ctx.PayrollInvoices.Count(a => 
            a.UserId == user.Id && (a.State == VendorPayInvoiceState.Completed || a.State == VendorPayInvoiceState.Cancelled));

        string invoiceDeleteText = completedOrCancelledInvoices > 0
            ? $"The user: {user.Name} will be deleted along with {completedOrCancelledInvoices} associated invoices. Are you sure you want to proceed?" : 
            $"The user: {user.Name} will be deleted. Are you sure?";

        return View("Confirm", new ConfirmModel($"Delete user", invoiceDeleteText, "Delete"));
    }


    [HttpPost("delete/{userId}")]
    public async Task<IActionResult> DeletePost(string userId)
    {
        if (CurrentStore is null)
            return NotFound();

        await using var ctx = vendorpayPluginDbContextFactory.CreateContext();
        var vendorpayUser = ctx.PayrollUsers
            .Single(a => a.Id == userId && a.StoreId == CurrentStore.Id);

        var userInvoices = ctx.PayrollInvoices.Where(a => a.UserId == vendorpayUser.Id).ToList();
        if (userInvoices.Any(a => a.State != VendorPayInvoiceState.Completed && a.State != VendorPayInvoiceState.Cancelled))
        {
            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Message = $"User can't be deleted since there are active invoices for this user",
                Severity = StatusMessageModel.StatusSeverity.Error
            });
            return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
        }
        var vendorpayUserInvite = ctx.PayrollInvitations.Where(c => c.Email == vendorpayUser.Email && c.StoreId == vendorpayUser.StoreId).ToList();
        if (vendorpayUserInvite.Any())
        {
            ctx.RemoveRange(vendorpayUserInvite);
        }
        ctx.RemoveRange(userInvoices);
        ctx.Remove(vendorpayUser);
        await ctx.SaveChangesAsync();

        TempData.SetStatusMessageModel(new StatusMessageModel()
        {
            Message = $"User deleted successfully",
            Severity = StatusMessageModel.StatusSeverity.Success
        });
        return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
    }

    public string GenerateUniqueToken()
    {
        byte[] tokenData = new byte[32];
        RandomNumberGenerator.Fill(tokenData);
        return Convert.ToBase64String(tokenData)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }
}