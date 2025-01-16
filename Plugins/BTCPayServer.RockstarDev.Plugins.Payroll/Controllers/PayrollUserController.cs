using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Text;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.RockstarDev.Plugins.Payroll.ViewModels;
using System.Security.Cryptography;
using BTCPayServer.RockstarDev.Plugins.Payroll.Services;
using BTCPayServer.Services.Stores;
using static QRCoder.PayloadGenerator;
using Microsoft.EntityFrameworkCore.Metadata;
using NBitpayClient;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Controllers;

[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class PayrollUserController(
    PayrollPluginDbContextFactory payrollPluginDbContextFactory, 
    StoreRepository storeRepo,
    VendorPayPassHasher hasher,
    IFileService fileService,
    EmailService emailService,
    HttpClient httpClient)
    : Controller
{
    private readonly HttpClient _httpClient = httpClient ?? new HttpClient();

    public StoreData CurrentStore => HttpContext.GetStoreData();


    [HttpGet("~/plugins/{storeId}/payroll/users")]
    public async Task<IActionResult> List(string storeId, bool all, bool pending)
    {
        await using var ctx = payrollPluginDbContextFactory.CreateContext();
        IQueryable<PayrollUser> query = ctx.PayrollUsers.Where(a => a.StoreId == storeId);
        if (pending)
        {
            query = query.Where(a => a.State == PayrollUserState.Pending);
        }
        else if (!all)
        {
            query = query.Where(a => a.State == PayrollUserState.Active);
        }
        List<PayrollUser> payrollUsers = await query
            .OrderByDescending(data => data.Name).ToListAsync();
        var payrollUserListViewModel = new PayrollUserListViewModel
        {
            All = all,
            Pending = pending,
            PayrollUsers = payrollUsers
        };
        return View(payrollUserListViewModel);
    }

    [HttpGet("~/plugins/{storeId}/payroll/users/create")]
    public IActionResult Create()
    {
        return View(new PayrollUserCreateViewModel());
    }

    [HttpPost("~/plugins/{storeId}/payroll/users/create")]
    public async Task<IActionResult> Create(PayrollUserCreateViewModel model)
    {
        try
        {
            if (CurrentStore is null)
                return NotFound();

            await using var dbPlugins = payrollPluginDbContextFactory.CreateContext();
            var userInDb = dbPlugins.PayrollUsers.AnyAsync(a => a.StoreId == CurrentStore.Id && a.Email == model.Email.ToLowerInvariant() && a.State != PayrollUserState.Pending);
            if (userInDb != null)
                ModelState.AddModelError(nameof(model.Email), "User with the same email already exists");

            if (!ModelState.IsValid)
                return View(model);

            string message = "User created successfully";
            var uid = Guid.NewGuid().ToString();
            var dbUser = new PayrollUser
            { 
                Id = uid,
                Name = model.Name,
                Email = model.Email.ToLowerInvariant(),
                StoreId = CurrentStore.Id,
                State = PayrollUserState.Active
            };
            if (string.IsNullOrEmpty(model.Password))
            {
                message = "An invitation has been sent to the user";
                dbUser.State = PayrollUserState.Pending;
                dbPlugins.Add(dbUser);
                var existingInvitation = dbPlugins.PayrollInvitations.SingleOrDefault(i => i.Email == model.Email && i.StoreId == CurrentStore.Id && !i.AcceptedAt.HasValue);
                if (existingInvitation != null)
                {
                    message = "An invitation has already been sent to this user";
                    return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
                }
                var invitation = new PayrollInvitation
                {
                    Id = uid,
                    StoreId = CurrentStore.Id,
                    Email = model.Email.ToLowerInvariant(),
                    Name = model.Name,
                    Token = GenerateUniqueToken(),
                    CreatedAt = DateTime.UtcNow
                };
                dbPlugins.Add(invitation);
                await SendUserInvitationEmail(dbUser, invitation.Token);
            }
            else
            {
                dbUser.Password = hasher.HashPassword(uid, model.Password);
                dbPlugins.Add(dbUser);
            }
            await dbPlugins.SaveChangesAsync();
            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Message = message,
                Severity = StatusMessageModel.StatusSeverity.Success
            });
            return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
        }
        catch (Exception)
        {
            ModelState.AddModelError(nameof(model.Email), "To generate an invite link, Kindly setup a correct Email SMTP service on your admin setting");
            return View(model);
        }
    }

    [HttpGet("~/plugins/payroll/users/resend-invitation/{userId}")]
    public async Task<IActionResult> ResendInvitation(string userId)
    {
        try
        {
            if (CurrentStore is null)
                return NotFound();

            await using var ctx = payrollPluginDbContextFactory.CreateContext();
            PayrollUser user = ctx.PayrollUsers.SingleOrDefault(a => a.Id == userId && a.StoreId == CurrentStore.Id);

            var existingInvitation = ctx.PayrollInvitations.FirstOrDefault(i => i.Email == user.Email && i.StoreId == CurrentStore.Id);
            existingInvitation.Token = GenerateUniqueToken();
            existingInvitation.CreatedAt = DateTime.UtcNow;
            ctx.Update(existingInvitation);
            await ctx.SaveChangesAsync();
            await SendUserInvitationEmail(user, existingInvitation.Token);
            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Message = $"User invitation resent successfully",
                Severity = StatusMessageModel.StatusSeverity.Success
            });
            return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id, pending = true });
        }
        catch (Exception)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Message = $"To generate an invite link, Kindly setup a correct Email SMTP service on your admin setting",
                Severity = StatusMessageModel.StatusSeverity.Error
            });
            return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
        }
    }

    private async Task SendUserInvitationEmail(PayrollUser model, string token)
    {
        var storeData = await storeRepo.FindStore(CurrentStore.Id);
        var invitationLink = Url.Action("AcceptInvitation", "Public", new { storeId = CurrentStore.Id, token }, Request.Scheme);
        await emailService.SendUserInvitationEmailEmail(model.Email, model.Name, storeData.StoreName, invitationLink);
    }


    [HttpGet("~/plugins/payroll/users/edit/{userId}")]
    public async Task<IActionResult> Edit(string userId)
    {
        if (CurrentStore is null)
            return NotFound();

        await using var ctx = payrollPluginDbContextFactory.CreateContext();

        var user = ctx.PayrollUsers.SingleOrDefault(a => a.Id == userId && a.StoreId == CurrentStore.Id);
        if (user.State == PayrollUserState.Pending)
            return NotFound();

        var model = new PayrollUserCreateViewModel { Id = user.Id, Email = user.Email, Name = user.Name };
        return View(model);
    }

    [HttpPost("~/plugins/payroll/users/edit/{userId}")]
    public async Task<IActionResult> Edit(string userId, PayrollUserCreateViewModel model)
    {
        if (CurrentStore is null)
            return NotFound();

        await using var ctx = payrollPluginDbContextFactory.CreateContext();
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

        await using var ctx = payrollPluginDbContextFactory.CreateContext();

        PayrollUser user = ctx.PayrollUsers.SingleOrDefault(a => a.Id == userId && a.StoreId == CurrentStore.Id);
        if (user.State == PayrollUserState.Pending)
            return NotFound();

        PayrollUserResetPasswordViewModel model = new PayrollUserResetPasswordViewModel { Id = user.Id };
        return View(model);
    }

    [HttpPost("~/plugins/payroll/users/resetpassword/{userId}")]
    public async Task<IActionResult> ResetPassword(string userId, PayrollUserResetPasswordViewModel model)
    {
        if (CurrentStore is null)
            return NotFound();

        await using var ctx = payrollPluginDbContextFactory.CreateContext();

        PayrollUser user = ctx.PayrollUsers.SingleOrDefault(a => a.Id == userId && a.StoreId == CurrentStore.Id);

        if (!ModelState.IsValid)
            return View(model);

        var passHashed = hasher.HashPassword(user.Id, model.NewPassword);
        user.Password = passHashed;

        ctx.Update(user);
        await ctx.SaveChangesAsync();

        ReturnMessageStatus();
        return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
    }

    [HttpGet("~/plugins/payroll/users/toggle/{userId}")]
    public async Task<IActionResult> ToggleUserStatus(string userId, bool enable)
    {
        if (CurrentStore is null)
            return NotFound();

        await using var ctx = payrollPluginDbContextFactory.CreateContext();
        PayrollUser user = ctx.PayrollUsers.SingleOrDefault(a => a.Id == userId && a.StoreId == CurrentStore.Id);

        if (user == null)
            return NotFound();

        return View("Confirm", new ConfirmModel($"{(enable ? "Activate" : "Disable")} user", $"The user ({user.Name}) will be {(enable ? "activated" : "disabled")}. Are you sure?", (enable ? "Activate" : "Disable")));
    }


    [HttpPost("~/plugins/payroll/users/toggle/{userId}")]
    public async Task<IActionResult> ToggleUserStatusPost(string userId, bool enable)
    {
        if (CurrentStore is null)
            return NotFound();

        await using var ctx = payrollPluginDbContextFactory.CreateContext();
        PayrollUser user = ctx.PayrollUsers.SingleOrDefault(a => a.Id == userId && a.StoreId == CurrentStore.Id);

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

    [HttpGet("~/plugins/payroll/users/downloadinvoices/{userId}")]
    public async Task<IActionResult> DownloadInvoices(string userId)
    {
        if (CurrentStore is null)
            return NotFound();

        await using var ctx = payrollPluginDbContextFactory.CreateContext();
        PayrollUser user = ctx.PayrollUsers.Single(a => a.Id == userId && a.StoreId == CurrentStore.Id);

        var payrollInvoices = await ctx.PayrollInvoices
            .Include(c => c.User)
            .Where(p => p.UserId == user.Id)
            .OrderByDescending(data => data.CreatedAt).ToListAsync();

        
        var zipName = $"Invoices-{user.Name}-{DateTime.Now:yyyy_MM_dd-HH_mm_ss}.zip";

        using var ms = new MemoryStream();
        using var zip = new ZipArchive(ms, ZipArchiveMode.Create, true);

        if (payrollInvoices.Count > 0)
        {
            var csvData = new StringBuilder();
            csvData.AppendLine("Name,Destination,Amount,Currency,Description,Status");
            foreach (var invoice in payrollInvoices)
            {
                csvData.AppendLine(
                    $"{invoice.User.Name},{invoice.Destination},{invoice.Amount},{invoice.Currency},{invoice.Description},{invoice.State}");

                var fileUrl =
                    await fileService.GetFileUrl(HttpContext.Request.GetAbsoluteRootUri(), invoice.InvoiceFilename);
                var fileBytes = await _httpClient.DownloadFileAsByteArray(fileUrl);
                string filename = Path.GetFileName(fileUrl);
                string extension = Path.GetExtension(filename);
                var entry = zip.CreateEntry($"{filename}{extension}");
                using (var entryStream = entry.Open())
                {
                    await entryStream.WriteAsync(fileBytes, 0, fileBytes.Length);
                }
            }

            var csv = zip.CreateEntry($"Invoices-{DateTime.Now:yyyy_MM_dd-HH_mm_ss}.csv");
            await using (var entryStream = csv.Open())
            {
                var csvBytes = Encoding.UTF8.GetBytes(csvData.ToString());
                await entryStream.WriteAsync(csvBytes);
            }
        }

        return File(ms.ToArray(), "application/zip", zipName);
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

        await using var ctx = payrollPluginDbContextFactory.CreateContext();
        PayrollUser user = ctx.PayrollUsers.SingleOrDefault(a => a.Id == userId && a.StoreId == CurrentStore.Id);

        if (user == null)
            return NotFound();

        var userHasInvoice = ctx.PayrollInvoices.Any(a => a.UserId == user.Id);
        if (userHasInvoice)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Message = $"User can't be deleted since there are active invoices for this user",
                Severity = StatusMessageModel.StatusSeverity.Error
            });
            return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
        }

        return View("Confirm", new ConfirmModel($"Delete user", $"The user: {user.Name} will be deleted. Are you sure?", "Delete"));
    }


    [HttpPost("~/plugins/payroll/users/delete/{userId}")]
    public async Task<IActionResult> DeletePost(string userId)
    {
        if (CurrentStore is null)
            return NotFound();

        await using var ctx = payrollPluginDbContextFactory.CreateContext();
        var payrollUser = ctx.PayrollUsers
            .SingleOrDefault(a => a.Id == userId && a.StoreId == CurrentStore.Id);

        var userHasInvoice = ctx.PayrollInvoices.Any(a =>
        a.UserId == payrollUser.Id);
        if (userHasInvoice)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Message = $"User can't be deleted since there are active invoices for this user",
                Severity = StatusMessageModel.StatusSeverity.Error
            });
            return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
        }

        var payrollUserInvite = ctx.PayrollInvitations.SingleOrDefault(c => c.Email == payrollUser.Email && c.StoreId == payrollUser.StoreId);
        if (payrollUserInvite != null)
        {
            ctx.Remove(payrollUserInvite);
        }
        ctx.Remove(payrollUser);
        await ctx.SaveChangesAsync();

        TempData.SetStatusMessageModel(new StatusMessageModel()
        {
            Message = $"User deleted successfully",
            Severity = StatusMessageModel.StatusSeverity.Success
        });
        return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
    }

    private string GenerateUniqueToken()
    {
        byte[] tokenData = new byte[32];
        RandomNumberGenerator.Fill(tokenData);
        return Convert.ToBase64String(tokenData)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }
}