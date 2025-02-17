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

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Controllers;

[Route("~/plugins/{storeId}/vendorpay/users/", Order = 0)]
[Route("~/plugins/{storeId}/payroll/users/", Order = 1)]
[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class PayrollUserController(
    PayrollPluginDbContextFactory payrollPluginDbContextFactory,
    VendorPayPassHasher hasher,
    IFileService fileService,
    HttpClient httpClient)
    : Controller
{
    private readonly HttpClient _httpClient = httpClient ?? new HttpClient();

    public StoreData CurrentStore => HttpContext.GetStoreData();


    [HttpGet("list")]
    public async Task<IActionResult> List(string storeId, bool all)
    {
        await using var ctx = payrollPluginDbContextFactory.CreateContext();
        List<PayrollUser> payrollUsers = await ctx.PayrollUsers
            .Where(a => a.StoreId == storeId)
            .OrderByDescending(data => data.Name).ToListAsync();

        var payrollUserListViewModel = new PayrollUserListViewModel
        {
            All = all,
            AllPayrollUsers = payrollUsers,
            DisplayedPayrollUsers = all ? payrollUsers : payrollUsers.Where(a => a.State == PayrollUserState.Active).ToList()
        };
        return View(payrollUserListViewModel);
    }

    [HttpGet("create")]
    public IActionResult Create()
    {
        return View(new PayrollUserCreateViewModel());
    }

    [HttpPost("create")]

    public async Task<IActionResult> Create(PayrollUserCreateViewModel model)
    {
        if (CurrentStore is null)
            return NotFound();

        await using var dbPlugins = payrollPluginDbContextFactory.CreateContext();

        var userInDb = dbPlugins.PayrollUsers.SingleOrDefault(a =>
            a.StoreId == CurrentStore.Id && a.Email == model.Email.ToLowerInvariant());
        if (userInDb != null)
            ModelState.AddModelError(nameof(model.Email), "User with the same email already exists");

        if (!ModelState.IsValid)
            return View(model);

        var uid = Guid.NewGuid().ToString();

        var passHashed = hasher.HashPassword(uid, model.Password);

        var dbUser = new PayrollUser
        {
            Id = uid,
            Name = model.Name,
            Email = model.Email.ToLowerInvariant(),
            Password = passHashed,
            StoreId = CurrentStore.Id,
            State = PayrollUserState.Active
        };

        dbPlugins.Add(dbUser);
        await dbPlugins.SaveChangesAsync();

        return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
    }

    [HttpGet("edit/{userId}")]
    public async Task<IActionResult> Edit(string userId)
    {
        if (CurrentStore is null)
            return NotFound();

        await using var ctx = payrollPluginDbContextFactory.CreateContext();

        var user = ctx.PayrollUsers.SingleOrDefault(a => a.Id == userId && a.StoreId == CurrentStore.Id);
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

        ctx.Remove(payrollUser);
        await ctx.SaveChangesAsync();

        TempData.SetStatusMessageModel(new StatusMessageModel()
        {
            Message = $"User deletion was successful",
            Severity = StatusMessageModel.StatusSeverity.Success
        });
        return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
    }
}