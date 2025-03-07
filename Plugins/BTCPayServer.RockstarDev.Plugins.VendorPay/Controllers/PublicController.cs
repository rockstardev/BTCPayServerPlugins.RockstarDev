using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data.Models;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.RockstarDev.Plugins.Payroll.Services.Helpers;
using BTCPayServer.RockstarDev.Plugins.Payroll.ViewModels;
using Newtonsoft.Json;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Controllers;

[AllowAnonymous]
[Route("~/plugins/{storeId}/vendorpay/public/", Order = 0)]
[Route("~/plugins/{storeId}/payroll/public/", Order = 1)]
public class PublicController(
    ApplicationDbContextFactory dbContextFactory,
    PluginDbContextFactory PluginDbContextFactory,
    IHttpContextAccessor httpContextAccessor,
    UriResolver uriResolver,
    VendorPayPassHasher hasher,
    PayrollInvoiceUploadHelper payrollInvoiceUploadHelper,
    InvoicesDownloadHelper invoicesDownloadHelper)
    : Controller
{
    private const string PAYROLL_AUTH_USER_ID = "PAYROLL_AUTH_USER_ID";


    [HttpGet("login")]
    public async Task<IActionResult> Login(string storeId)
    {
        var vali = await validateStoreAndUser(storeId, false);
        if (vali.ErrorActionResult != null)
            return vali.ErrorActionResult;

        var model = new PublicLoginViewModel
        {
            StoreName = vali.Store.StoreName,
            StoreBranding = await StoreBrandingViewModel.CreateAsync(Request, uriResolver, vali.Store.GetStoreBlob())
        };

        return View(model);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(string storeId, PublicLoginViewModel model)
    {
        var vali = await validateStoreAndUser(storeId, false);
        if (vali.ErrorActionResult != null)
            return vali.ErrorActionResult;

        model.StoreId = vali.Store.Id;
        model.StoreName = vali.Store.StoreName;
        model.StoreBranding = await StoreBrandingViewModel.CreateAsync(Request, uriResolver, vali.Store.GetStoreBlob());

        await using var dbPlugins = PluginDbContextFactory.CreateContext();
        var userInDb = dbPlugins.PayrollUsers.SingleOrDefault(a =>
            a.StoreId == storeId && a.Email == model.Email.ToLowerInvariant());

        if (userInDb != null)
        {
            if (userInDb.State == PayrollUserState.Active && hasher.IsValidPassword(userInDb, model.Password))
            {
                httpContextAccessor.HttpContext!.Session.SetString(PAYROLL_AUTH_USER_ID, userInDb!.Id);
                return RedirectToAction(nameof(ListInvoices), new { storeId });
            }
        }

        // if we end up here, credentials are invalid 
        ModelState.AddModelError(nameof(model.Password), "Invalid credentials");
        return View(model);
    }

    [HttpGet("users/{token}/accept")]
    public async Task<IActionResult> AcceptInvitation(string storeId, string token)
    {
        await using var dbPlugins = PluginDbContextFactory.CreateContext();

        var invitation = dbPlugins.PayrollInvitations
            .SingleOrDefault(i => i.Token == token && !i.AcceptedAt.HasValue &&
                        i.CreatedAt.AddDays(7) >= DateTime.UtcNow);
        if (invitation == null)
        {
            ModelState.AddModelError("NewPassword", "Invalid or expired invitation");
            return View(new AcceptInvitationRequestViewModel { Email = "", Token = token });
        }
        var dbUser = dbPlugins.PayrollUsers.SingleOrDefault(a => a.StoreId == invitation.StoreId && a.Email == invitation.Email.ToLowerInvariant()); 
        return View(new AcceptInvitationRequestViewModel
        {
            Email = dbUser?.Email,
            Token = token,
            Name = dbUser?.Name
        });
    }

    [HttpPost("users/{token}/accept")]
    public async Task<IActionResult> AcceptInvitation(AcceptInvitationRequestViewModel model)
    {
        await using var dbPlugins = PluginDbContextFactory.CreateContext();
        var invitation = dbPlugins.PayrollInvitations.SingleOrDefault(i => i.Token == model.Token && !i.AcceptedAt.HasValue && i.CreatedAt.AddDays(7) >= DateTime.UtcNow);
        if (invitation == null)
        {
            ModelState.AddModelError(nameof(model.NewPassword), "Invalid or expired invitation");
            return View(model);
        }
        var dbUser = dbPlugins.PayrollUsers.SingleOrDefault(a => a.StoreId == invitation.StoreId && a.Email == invitation.Email.ToLowerInvariant());
        if (dbUser == null)
        {
            ModelState.AddModelError(nameof(model.NewPassword), "User record does not exist. Kindly reach out to the BTCPay Server instance admin");
            return View(model);
        }
        if (dbUser.State != PayrollUserState.Pending)
        {
            ModelState.AddModelError(nameof(model.NewPassword), "User with the same email already exists. Kindly reach out to the BTCPay Server instance admin");
            return View(model);
        }
        if (!ModelState.IsValid)
            return View(model);

        dbUser.Password = hasher.HashPassword(invitation.Id, model.NewPassword);
        dbUser.State = PayrollUserState.Active;
        invitation.AcceptedAt = DateTime.UtcNow;
        dbPlugins.Update(dbUser);
        dbPlugins.Update(invitation);
        await dbPlugins.SaveChangesAsync();
        TempData.SetStatusMessageModel(new StatusMessageModel()
        {
            Message = "User created successfully",
            Severity = StatusMessageModel.StatusSeverity.Success
        });
        return RedirectToAction(nameof(Login), "Public", new { storeId = invitation.StoreId });
    }

    [HttpGet("logout")]
    public IActionResult Logout(string storeId)
    {
        httpContextAccessor.HttpContext?.Session.Remove(PAYROLL_AUTH_USER_ID);
        return redirectToLogin(storeId);
    }

    private RedirectToActionResult redirectToLogin(string storeId)
    {
        return RedirectToAction(nameof(Login), new { storeId });
    }

    [HttpGet("listinvoices")]
    public async Task<IActionResult> ListInvoices(string storeId)
    {
        var vali = await validateStoreAndUser(storeId, true);
        if (vali.ErrorActionResult != null)
            return vali.ErrorActionResult;

        await using var ctx = PluginDbContextFactory.CreateContext();
        var payrollInvoices = await ctx.PayrollInvoices
            .Include(data => data.User)
            .Where(p => p.User.StoreId == storeId && p.UserId == vali.UserId && p.IsArchived == false)
            .OrderByDescending(data => data.CreatedAt).ToListAsync();

        var settings = await ctx.GetSettingAsync(storeId);
        var model = new PublicListInvoicesViewModel
        {
            StoreId = vali.Store.Id,
            StoreName = vali.Store.StoreName,
            StoreBranding = await StoreBrandingViewModel.CreateAsync(Request, uriResolver, vali.Store.GetStoreBlob()),
            PurchaseOrdersRequired = settings.PurchaseOrdersRequired,
            Invoices = payrollInvoices.Select(tuple => new PayrollInvoiceViewModel()
            {
                CreatedAt = tuple.CreatedAt,
                Id = tuple.Id,
                Name = tuple.User.Name,
                Email = tuple.User.Email,
                Destination = tuple.Destination,
                Amount = tuple.Amount,
                Currency = tuple.Currency,
                State = tuple.State,
                TxnId = tuple.TxnId,
                PurchaseOrder = tuple.PurchaseOrder,
                Description = tuple.Description,
                InvoiceUrl = tuple.InvoiceFilename,
                ExtraInvoiceFiles = tuple.ExtraFilenames,
                PaidAt = tuple.PaidAt,
                AdminNote = null // not displaying admin notes publicaly
            }).ToList()
        };

        return View(model);
    }

    private async Task<StoreUserValidator> validateStoreAndUser(string storeId, bool validateUser)
    {
        await using var dbMain = dbContextFactory.CreateContext();
        var store = await dbMain.Stores.SingleOrDefaultAsync(a => a.Id == storeId);
        if (store == null)
            return new StoreUserValidator { ErrorActionResult = NotFound() };

        string userId = null;
        if (validateUser)
        {
            await using var dbPlugin = PluginDbContextFactory.CreateContext();
            userId = httpContextAccessor.HttpContext!.Session.GetString(PAYROLL_AUTH_USER_ID);
            var userInDb = dbPlugin.PayrollUsers.SingleOrDefault(a =>
                a.StoreId == storeId && a.Id == userId && a.State == PayrollUserState.Active);
            if (userInDb == null)
                return new StoreUserValidator { ErrorActionResult = redirectToLogin(storeId) };
            else
                userId = userInDb.Id;
        }

        return new StoreUserValidator { Store = store, UserId = userId };
    }
    private class StoreUserValidator
    {
        public IActionResult ErrorActionResult { get; set; }
        public StoreData Store { get; set; }
        public string UserId { get; set; }
    }
    
    
    [HttpGet("DownloadInvoices")]
    public async Task<IActionResult> DownloadInvoices(string storeId, string invoiceId)
    {
        var vali = await validateStoreAndUser(storeId, true);
        if (vali.ErrorActionResult != null)
            return vali.ErrorActionResult;

        await using var ctx = PluginDbContextFactory.CreateContext();
        var invoice = ctx.PayrollInvoices
            .Include(a => a.User)
            .Single(a => a.Id == invoiceId && a.User.StoreId == storeId);
        return await invoicesDownloadHelper.Process([invoice], HttpContext.Request.GetAbsoluteRootUri());
    }


    // upload
    [HttpGet("upload")]
    public async Task<IActionResult> Upload(string storeId)
    {
        var vali = await validateStoreAndUser(storeId, true);
        if (vali.ErrorActionResult != null)
            return vali.ErrorActionResult;

        var settings = await PluginDbContextFactory.GetSettingAsync(storeId);
        var model = new PublicPayrollInvoiceUploadViewModel
        {
            StoreId = vali.Store.Id,
            StoreName = vali.Store.StoreName,
            StoreBranding = await StoreBrandingViewModel.CreateAsync(Request, uriResolver, vali.Store.GetStoreBlob()),
            Amount = 0,
            Currency = vali.Store.GetStoreBlob().DefaultCurrency,
            PurchaseOrdersRequired = settings.PurchaseOrdersRequired
        };

        return View(model);
    }

    [HttpPost("upload")]
    public async Task<IActionResult> Upload(string storeId, PublicPayrollInvoiceUploadViewModel model)
    {
        var vali = await validateStoreAndUser(storeId, true);
        if (vali.ErrorActionResult != null)
            return vali.ErrorActionResult;

        model.StoreId = vali.Store.Id;
        model.StoreName = vali.Store.StoreName;
        model.StoreBranding = await StoreBrandingViewModel.CreateAsync(Request, uriResolver, vali.Store.GetStoreBlob());

        var validation = await payrollInvoiceUploadHelper.Process(storeId, vali.UserId, model);
        if (!validation.IsValid)
        {
            validation.ApplyToModelState(ModelState);
            return View(model);
        }

        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Message = "Invoice uploaded successfully",
            Severity = StatusMessageModel.StatusSeverity.Success
        });

        return RedirectToAction(nameof(ListInvoices), new { storeId });
    }

    // change password

    [HttpGet("changepassword")]
    public async Task<IActionResult> ChangePassword(string storeId)
    {
        var vali = await validateStoreAndUser(storeId, true);
        if (vali.ErrorActionResult != null)
            return vali.ErrorActionResult;

        var model = new PublicChangePasswordViewModel
        {
            StoreId = vali.Store.Id,
            StoreName = vali.Store.StoreName,
            StoreBranding = await StoreBrandingViewModel.CreateAsync(Request, uriResolver, vali.Store.GetStoreBlob())
        };

        return View(model);
    }

    [HttpPost("changepassword")]
    public async Task<IActionResult> ChangePassword(string storeId, PublicChangePasswordViewModel model)
    {
        var vali = await validateStoreAndUser(storeId, true);
        if (vali.ErrorActionResult != null)
            return vali.ErrorActionResult;

        model.StoreId = vali.Store.Id;
        model.StoreName = vali.Store.StoreName;
        model.StoreBranding = await StoreBrandingViewModel.CreateAsync(Request, uriResolver, vali.Store.GetStoreBlob());

        if (!ModelState.IsValid)
            return View(model);

        await using var dbPlugins = PluginDbContextFactory.CreateContext();
        var userInDb = dbPlugins.PayrollUsers.SingleOrDefault(a =>
            a.StoreId == storeId && a.Id == vali.UserId);
        if (userInDb == null)
            ModelState.AddModelError(nameof(model.CurrentPassword), "Invalid password");

        if (!hasher.IsValidPassword(userInDb, model.CurrentPassword))
            ModelState.AddModelError(nameof(model.CurrentPassword), "Invalid password");

        if (!ModelState.IsValid)
            return View(model);



        // 
        userInDb!.Password = hasher.HashPassword(vali.UserId, model.NewPassword);
        await dbPlugins.SaveChangesAsync();

        //
        TempData.SetStatusMessageModel(new StatusMessageModel()
        {
            Message = $"Password successfully changed",
            Severity = StatusMessageModel.StatusSeverity.Success
        });
        return RedirectToAction(nameof(ListInvoices), new { storeId });
    }
    
    //
    

    [HttpGet("delete/{id}")]
    public async Task<IActionResult> Delete(string storeId, string id)
    {
        var vali = await validateStoreAndUser(storeId, true);
        if (vali.ErrorActionResult != null)
            return vali.ErrorActionResult;

        await using var ctx = PluginDbContextFactory.CreateContext();
        PayrollInvoice invoice = ctx.PayrollInvoices.Include(c => c.User)
            .SingleOrDefault(a => a.Id == id);

        if (invoice == null)
            return NotFound();

        if (invoice.State != PayrollInvoiceState.AwaitingApproval)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = $"Invoice cannot be deleted as it has been actioned upon",
                Severity = StatusMessageModel.StatusSeverity.Error
            });
            return RedirectToAction(nameof(ListInvoices), new { storeId = vali.Store.Id });
        }
        return View("Confirm", new ConfirmModel($"Delete Invoice", $"Do you really want to delete the invoice for {invoice.Amount} {invoice.Currency} from {invoice.User.Name}?", "Delete"));
    }

    [HttpPost("delete/{id}")]
    public async Task<IActionResult> DeletePost(string storeId, string id)
    {
        var vali = await validateStoreAndUser(storeId, true);
        if (vali.ErrorActionResult != null)
            return vali.ErrorActionResult;

        await using var ctx = PluginDbContextFactory.CreateContext();

        var invoice = ctx.PayrollInvoices.Single(a => a.Id == id);

        if (invoice.State != PayrollInvoiceState.AwaitingApproval)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = $"Invoice cannot be deleted as it has been actioned upon",
                Severity = StatusMessageModel.StatusSeverity.Error
            });
            return RedirectToAction(nameof(ListInvoices), new { storeId = storeId });
        }

        ctx.Remove(invoice);
        await ctx.SaveChangesAsync();

        TempData.SetStatusMessageModel(new StatusMessageModel()
        {
            Message = $"Invoice deleted successfully",
            Severity = StatusMessageModel.StatusSeverity.Success
        });
        return RedirectToAction(nameof(ListInvoices), new { storeId = storeId });
    } 
}