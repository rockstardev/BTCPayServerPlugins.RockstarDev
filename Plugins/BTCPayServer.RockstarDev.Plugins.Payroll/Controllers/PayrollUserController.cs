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
using BTCPayServer.Services.Stores;
using BTCPayServer.RockstarDev.Plugins.Payroll.Services;
using System.Security.Cryptography;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Controllers;

[Route("~/plugins/{storeId}/payroll/users/")]
[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class PayrollUserController(
    PayrollPluginDbContextFactory payrollPluginDbContextFactory,
    StoreRepository storeRepo,
    VendorPayPassHasher hasher,
    EmailService emailService,
    IFileService fileService,
    HttpClient httpClient)
    : Controller
{
    private readonly HttpClient _httpClient = httpClient ?? new HttpClient();

    public StoreData CurrentStore => HttpContext.GetStoreData();


    [HttpGet("list")]
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
        List<PayrollUser> payrollUsers = query.OrderByDescending(data => data.Name).ToList();
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

        var settings = await payrollPluginDbContextFactory.GetSettingAsync(CurrentStore.Id);
        var vm = new PayrollUserCreateViewModel { EmailInviteForUsers = settings?.EmailInviteForUsers ?? false, StoreId = CurrentStore.Id };
        return View(vm);
    }

    [HttpPost("create")]
    public async Task<IActionResult> Create(PayrollUserCreateViewModel model)
    {
        if (CurrentStore is null)
            return NotFound();

        var settings = await payrollPluginDbContextFactory.GetSettingAsync(CurrentStore.Id);
        await using var dbPlugins = payrollPluginDbContextFactory.CreateContext();

        var email = model.Email.ToLowerInvariant();

        if (await dbPlugins.PayrollUsers
            .AnyAsync(a => a.StoreId == CurrentStore.Id && a.Email == email && a.State != PayrollUserState.Pending))
        {
            ModelState.AddModelError(nameof(model.Email), "User with the same email already exists");
            return View(model);
        }

        bool emailUserInvite = settings?.EmailInviteForUsers ?? false;
        var uid = Guid.NewGuid().ToString();
        var dbUser = new PayrollUser
        {
            Id = uid,
            Name = model.Name,
            Email = email,
            StoreId = CurrentStore.Id,
            State = emailUserInvite ? PayrollUserState.Pending : PayrollUserState.Active
        };
        if (emailUserInvite)
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
            var invitation = new PayrollInvitation
            {
                Id = uid,
                StoreId = CurrentStore.Id,
                Email = email,
                Name = model.Name,
                Token = GenerateUniqueToken(),
                CreatedAt = DateTime.UtcNow
            };
            dbPlugins.Add(dbUser);
            dbPlugins.Add(invitation);
            await dbPlugins.SaveChangesAsync();
            try
            {
                await SendUserInvitationEmail(dbUser, invitation.Token);
            }
            catch (Exception)
            {
                ModelState.AddModelError(nameof(model.Email), "To generate an invite link, Kindly setup a correct Email SMTP service on your admin setting");
                return View(model);
            }
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = "An invitation has been sent to the user",
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

    [HttpPost]
    public async Task<IActionResult> PreviewInvitationEmail([FromForm] PayrollUserCreateViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest("Invalid model data");
        }

        var storeData = await storeRepo.FindStore(CurrentStore.Id);
        var invitationLink = Url.Action("AcceptInvitation", "Public", new { storeId = CurrentStore.Id, token = Guid.NewGuid().ToString() }, Request.Scheme);
        var templateContent = emailService.GetEmbeddedResourceContent("Templates.InvitationEmail.cshtml");
        string emailBody = templateContent
                            .Replace("@Model.Name", model.Name)
                            .Replace("@Model.StoreName", storeData.StoreName)
                            .Replace("@Model.VendorPayRegisterLink", invitationLink);
        return Content(emailBody, "text/html");
    }

    [HttpGet("~/plugins/payroll/users/resend-invitation/{userId}")]
    public async Task<IActionResult> ResendInvitation(string userId)
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
        try
        {
            await SendUserInvitationEmail(user, existingInvitation.Token);
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
        TempData.SetStatusMessageModel(new StatusMessageModel()
        {
            Message = $"User invitation resent successfully",
            Severity = StatusMessageModel.StatusSeverity.Success
        });
        return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id, pending = true });
    }


    [HttpGet("edit/{userId}")]
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

    [HttpPost("edit/{userId}")]
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


    [HttpGet("resetpassword/{userId}")]
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

    [HttpPost("resetpassword/{userId}")]
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

    [HttpGet("toggle/{userId}")]
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


    [HttpPost("toggle/{userId}")]
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

    [HttpGet("downloadinvoices/{userId}")]
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

    [HttpGet("delete/{userId}")]
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


    [HttpPost("delete/{userId}")]
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
        var payrollUserInvite = ctx.PayrollInvitations.Where(c => c.Email == payrollUser.Email && c.StoreId == payrollUser.StoreId).ToList();
        if (payrollUserInvite.Any())
        {
            ctx.RemoveRange(payrollUserInvite);
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

    public string GenerateUniqueToken()
    {
        byte[] tokenData = new byte[32];
        RandomNumberGenerator.Fill(tokenData);
        return Convert.ToBase64String(tokenData)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }

    private async Task SendUserInvitationEmail(PayrollUser model, string token)
    {
        var storeData = await storeRepo.FindStore(CurrentStore.Id);
        var invitationLink = Url.Action("AcceptInvitation", "Public", new { storeId = CurrentStore.Id, token }, Request.Scheme);
        await emailService.SendUserInvitationEmailEmail(new InvitationEmailModel
        {
            StoreId = CurrentStore.Id,
            StoreName = storeData.StoreName,
            Subject = "You are invited to create a Vendor Pay account",
            UserEmail = model.Email,
            UserName = model.Name,
            VendorPayRegisterLink = invitationLink,
        });
    }
}