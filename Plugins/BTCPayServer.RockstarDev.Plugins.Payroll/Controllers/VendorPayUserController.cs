using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Data;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Data;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Data.Models;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Security;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Services;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Services.Helpers;
using BTCPayServer.RockstarDev.Plugins.VendorPay.ViewModels;
using BTCPayServer.Services.Labels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.RockstarDev.Plugins.VendorPay.Controllers;

[Route("~/plugins/{storeId}/vendorpay/users/", Order = 0)]
[Route("~/plugins/{storeId}/payroll/users/", Order = 1)]
[Authorize(Policy = VendorPayPermissions.UsersManage, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class VendorPayUserController(
    PluginDbContextFactory pluginDbContextFactory,
    VendorPayPassHasher hasher,
    EmailService emailService,
    StoreLabelRepository storeLabelRepository,
    InvoicesDownloadHelper invoicesDownloadHelper)
    : Controller
{
    public StoreData CurrentStore => HttpContext.GetStoreData();

    [HttpGet("labels")]
    public async Task<IActionResult> ManageLabels(string storeId)
    {
        var labels = await storeLabelRepository.GetStoreLabels(storeId, VendorPayPluginConst.LabelType);
        var vm = new VendorPayLabelsViewModel
        {
            StoreId = storeId,
            Labels = labels.Select(l => new VendorPayLabelViewModel
            {
                Label = l.Label,
                Color = l.Color,
                TextColor = ColorPalette.Default.TextColor(l.Color)
            }).ToList()
        };
        return View(vm);
    }


    [HttpGet("labels/{id}/delete")]
    public IActionResult DeleteLabel(string storeId, string id)
    {
        return View("Confirm", new ConfirmModel("Delete Label", $"The label ({id}) will be deleted. Are you sure?", "Delete"));
    }


    [HttpPost("labels/{id}/delete")]
    public async Task<IActionResult> DeleteLabelPost(string storeId, string id)
    {
        await storeLabelRepository.RemoveStoreLabels(storeId, VendorPayPluginConst.LabelType, new[] { id });
        TempData[WellKnownTempData.SuccessMessage] = "Label deleted successfully";
        return RedirectToAction(nameof(ManageLabels), new { storeId });
    }

    [HttpPost("labels/{id}/edit")]
    public async Task<IActionResult> EditLabel(string storeId, string id, string newLabel)
    {
        if (string.IsNullOrWhiteSpace(newLabel))
        {
            TempData[WellKnownTempData.ErrorMessage] = "Label name cannot be empty";
            return RedirectToAction(nameof(ManageLabels), new { storeId });
        }
        newLabel = newLabel.Trim();
        if (newLabel == id)
            return RedirectToAction(nameof(ManageLabels), new { storeId });

        var ok = await storeLabelRepository.RenameStoreLabel(storeId, VendorPayPluginConst.LabelType, id, newLabel);
        TempData[ok ? WellKnownTempData.SuccessMessage : WellKnownTempData.ErrorMessage] = ok ? "Label renamed successfully." : "Label could not be renamed.";
        return RedirectToAction(nameof(ManageLabels), new { storeId });
    }

    [HttpGet("list")]
    public async Task<IActionResult> List(string storeId, bool all, bool pending, bool oneTime, string searchTerm = null, string label = null)
    {
        await using var ctx = pluginDbContextFactory.CreateContext();

        var query = ctx.PayrollUsers.Where(a => a.StoreId == storeId);

        var counts = query.GroupBy(_ => 1)
        .Select(g => new VendorPayUserListViewModel.CountsData
        {
            Active = g.Count(a => a.State == VendorPayUserState.Active),
            Pending = g.Count(a => a.State == VendorPayUserState.Pending),
            OneTime = g.Count(a => a.State == VendorPayUserState.OneTime),
            Total = g.Count()
        })
        .FirstOrDefault() ?? new VendorPayUserListViewModel.CountsData();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var search = searchTerm.Trim().ToLower();
            query = query.Where(u => u.Name.ToLower().Contains(search) || u.Email.ToLower().Contains(search));
        }

        var filteredQuery = (pending, oneTime, all) switch
        {
            (true, _, _) => query.Where(a => a.State == VendorPayUserState.Pending),
            (_, true, _) => query.Where(a => a.State == VendorPayUserState.OneTime),
            (_, _, false) => query.Where(a => a.State == VendorPayUserState.Active),
            _ => query
        };
        var vendorPayUsers = filteredQuery.OrderBy(a => a.Name).ToList();

        var allStoreUserIds = ctx.PayrollUsers.Where(a => a.StoreId == storeId).Select(u => u.Id).ToArray();
        var allUserLabels = await storeLabelRepository.GetStoreLabelsForObjects(storeId, VendorPayPluginConst.LabelType, allStoreUserIds);

        var allLabels = allUserLabels.SelectMany(kv => kv.Value)
            .GroupBy(l => l.Label, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Label: g.Key, Color: g.First().Color, Count: g.Count()))
            .OrderBy(l => l.Label).ToArray();

        var displayedUserIds = vendorPayUsers.Select(u => u.Id).ToHashSet();
        var labelsForUsers = allUserLabels.Where(kv => displayedUserIds.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);

        if (!string.IsNullOrEmpty(label))
        {
            var labelledIds = allUserLabels.Where(kv => kv.Value.Any(l => l.Label.Equals(label, StringComparison.OrdinalIgnoreCase)))
                .Select(kv => kv.Key).ToHashSet();

            vendorPayUsers = vendorPayUsers.Where(u => labelledIds.Contains(u.Id)).ToList();
            labelsForUsers = labelsForUsers.Where(kv => labelledIds.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
        }
        var vendorPayUserListViewModel = new VendorPayUserListViewModel
        {
            All = all,
            Pending = pending,
            OneTime = oneTime,
            SearchTerm = searchTerm,
            DisplayedVendorPayUsers = vendorPayUsers,
            ActiveLabel = label,
            AllLabels = allLabels,
            LabelsPerUser = labelsForUsers,
            Counts = counts
        };
        return View(vendorPayUserListViewModel);
    }

    [HttpGet("create")]
    public async Task<IActionResult> Create()
    {
        if (CurrentStore is null)
            return NotFound();

        var settings = await pluginDbContextFactory.GetSettingAsync(CurrentStore.Id);
        var isEmailSettingsConfigured = await emailService.IsEmailSettingsConfigured(CurrentStore.Id);
        var vm = new VendorPayUserCreateViewModel
        {
            StoreId = CurrentStore.Id,
            UserInviteEmailBody = settings.UserInviteEmailBody ?? VendorPaySettingViewModel.Defaults.UserInviteEmailBody,
            UserInviteEmailSubject = settings.UserInviteEmailSubject ?? VendorPaySettingViewModel.Defaults.UserInviteEmailSubject,
            StoreEmailSettingsConfigured = isEmailSettingsConfigured
        };
        return View(vm);
    }

    [HttpPost("create")]
    public async Task<IActionResult> Create(VendorPayUserCreateViewModel model)
    {
        if (CurrentStore is null)
            return NotFound();

        await using var dbPlugins = pluginDbContextFactory.CreateContext();

        var email = model.Email.ToLowerInvariant();
        model.StoreEmailSettingsConfigured = await emailService.IsEmailSettingsConfigured(CurrentStore.Id);
        model.StoreId = CurrentStore.Id;

        if (model.SendRegistrationEmailInviteToUser && string.IsNullOrWhiteSpace(model.UserInviteEmailSubject))
        {
            ModelState.AddModelError(nameof(model.UserInviteEmailSubject), "Invite email subject cannot be empty");
        }

        if (model.SendRegistrationEmailInviteToUser && string.IsNullOrWhiteSpace(model.UserInviteEmailBody))
        {
            ModelState.AddModelError(nameof(model.UserInviteEmailBody), "Invite email body cannot be empty");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

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
                    Url.Action(nameof(PublicController.AcceptInvitation), "Public", new { storeId = CurrentStore.Id, invitation.Token }, Request.Scheme));
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

            if (model.Password != model.ConfirmPassword)
            {
                ModelState.AddModelError(nameof(model.ConfirmPassword), "Password fields don't match");
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
        if (model.Labels?.Count > 0)
            await storeLabelRepository.SetStoreObjectLabels(CurrentStore.Id, VendorPayPluginConst.LabelType, dbUser.Id, model.Labels.ToArray());

        return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
    }

    [HttpGet("resend-invitation/{userId}")]
    public async Task<IActionResult> ResendInvitation(string storeId, string userId)
    {
        if (CurrentStore is null)
            return NotFound();

        await using var ctx = pluginDbContextFactory.CreateContext();

        var user = ctx.PayrollUsers.SingleOrDefault(a => a.Id == userId && a.StoreId == CurrentStore.Id && a.State == VendorPayUserState.Pending);

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
            var settings = await ctx.GetSettingAsync(storeId);
            var userInviteEmailBody = string.IsNullOrWhiteSpace(settings.UserInviteEmailBody)
                ? VendorPaySettingViewModel.Defaults.UserInviteEmailBody
                : settings.UserInviteEmailBody;
            var userInviteEmailSubject = string.IsNullOrWhiteSpace(settings.UserInviteEmailSubject)
                ? VendorPaySettingViewModel.Defaults.UserInviteEmailSubject
                : settings.UserInviteEmailSubject;
            await emailService.SendUserInvitationEmail(user, userInviteEmailSubject, userInviteEmailBody,
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
        if (user.State == VendorPayUserState.Pending)
            return NotFound();

        var labelsForUser = await storeLabelRepository.GetStoreLabelsForObjects(CurrentStore.Id, VendorPayPluginConst.LabelType, new[] { userId });

        var model = new VendorPayUserCreateViewModel
        {
            Id = user.Id,
            Email = user.Email,
            Name = user.Name,
            EmailReminder = user.EmailReminder,
            StoreId = CurrentStore.Id,
            Labels = labelsForUser.TryGetValue(userId, out var ll) ? ll.Select(l => l.Label).ToList() : new List<string>(),
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
            model.StoreId = CurrentStore.Id;
            model.StoreEmailSettingsConfigured = await emailService.IsEmailSettingsConfigured(CurrentStore.Id);
            return View(model);
        }

        await using var ctx = pluginDbContextFactory.CreateContext();

        var user = ctx.PayrollUsers.SingleOrDefault(a => a.Id == userId && a.StoreId == CurrentStore.Id);

        user.Email = string.IsNullOrEmpty(model.Email) ? user.Email : model.Email;
        user.Name = string.IsNullOrEmpty(model.Name) ? user.Name : model.Name;

        user.EmailReminder = string.IsNullOrEmpty(model.EmailReminder)
            ? null
            : string.Join(",", model.EmailReminder.Split(',')
                .Select(r => r.Trim()).Where(r => !string.IsNullOrEmpty(r))
                .Distinct().OrderBy(int.Parse));

        ctx.Update(user);
        await ctx.SaveChangesAsync();
        await storeLabelRepository.SetStoreObjectLabels(CurrentStore.Id, VendorPayPluginConst.LabelType, userId, model.Labels?.ToArray() ?? Array.Empty<string>());

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
        if (user.State == VendorPayUserState.Pending)
            return NotFound();
        var model = new VendorPayUserResetPasswordViewModel { Id = user.Id };
        return View(model);
    }

    [HttpPost("resetpassword/{userId}")]
    public async Task<IActionResult> ResetPassword(string userId, VendorPayUserResetPasswordViewModel model)
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

        await using var ctx = pluginDbContextFactory.CreateContext();
        var user = ctx.PayrollUsers.Single(a => a.Id == userId && a.StoreId == CurrentStore.Id);

        var vendorPayInvoices = await ctx.PayrollInvoices
            .Include(c => c.User)
            .Where(p => p.UserId == user.Id)
            .OrderByDescending(data => data.CreatedAt).ToListAsync();
        if (!vendorPayInvoices.Any())
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = "No payroll invoice available for download", Severity = StatusMessageModel.StatusSeverity.Info
            });
            return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
        }

        return await invoicesDownloadHelper.Process(vendorPayInvoices, HttpContext.Request.GetAbsoluteRootUri());
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
                                         a.State != VendorPayInvoiceState.Completed && a.State != VendorPayInvoiceState.Cancelled))
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = "User can't be deleted since there are active invoices for this user", Severity = StatusMessageModel.StatusSeverity.Error
            });
            return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
        }

        var completedOrCancelledInvoices = ctx.PayrollInvoices.Count(a =>
            a.UserId == user.Id && (a.State == VendorPayInvoiceState.Completed || a.State == VendorPayInvoiceState.Cancelled));

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
        var vendorPayUser = ctx.PayrollUsers
            .Single(a => a.Id == userId && a.StoreId == CurrentStore.Id);

        var userInvoices = ctx.PayrollInvoices.Where(a => a.UserId == vendorPayUser.Id).ToList();
        if (userInvoices.Any(a => a.State != VendorPayInvoiceState.Completed && a.State != VendorPayInvoiceState.Cancelled))
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = "User can't be deleted since there are active invoices for this user", Severity = StatusMessageModel.StatusSeverity.Error
            });
            return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
        }

        var vendorPayUserInvite = ctx.PayrollInvitations.Where(c => c.Email == vendorPayUser.Email && c.StoreId == vendorPayUser.StoreId).ToList();
        if (vendorPayUserInvite.Any())
            ctx.RemoveRange(vendorPayUserInvite);
        ctx.RemoveRange(userInvoices);
        ctx.Remove(vendorPayUser);
        await ctx.SaveChangesAsync();
        await storeLabelRepository.SetStoreObjectLabels(CurrentStore.Id, VendorPayPluginConst.LabelType, userId, Array.Empty<string>());

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
