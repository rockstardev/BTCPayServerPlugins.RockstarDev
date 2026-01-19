using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Data;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Data.Models;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Services;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Services.Helpers;
using BTCPayServer.RockstarDev.Plugins.VendorPay.ViewModels;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.RockstarDev.Plugins.VendorPay.Controllers;

[AllowAnonymous]
[Route("~/plugins/{storeId}/vendorpay/public/", Order = 0)]
[Route("~/plugins/{storeId}/payroll/public/", Order = 1)]
public class PublicController(
    ApplicationDbContextFactory dbContextFactory,
    PluginDbContextFactory pluginDbContextFactory,
    IHttpContextAccessor httpContextAccessor,
    UriResolver uriResolver,
    VendorPayPassHasher hasher,
    VendorPayInvoiceUploadHelper vendorPayInvoiceUploadHelper,
    InvoicesDownloadHelper invoicesDownloadHelper,
    EmailService emailService)
    : Controller
{
    private const string VENDORPAY_AUTH_USER_ID = "PAYROLL_AUTH_USER_ID";

    private static string GenerateUploadHash(string storeId, string uploadCode)
    {
        var input = storeId + uploadCode;
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var hashString = Convert.ToHexString(hashBytes).ToLowerInvariant();
        return hashString[..12]; // First 12 characters
    }

    [HttpGet("login")]
    public async Task<IActionResult> Login(string storeId)
    {
        var vali = await validateStoreAndUser(storeId, false);
        if (vali.ErrorActionResult != null)
            return vali.ErrorActionResult;

        var model = new PublicLoginViewModel
        {
            StoreName = vali.Store.StoreName, StoreBranding = await StoreBrandingViewModel.CreateAsync(Request, uriResolver, vali.Store.GetStoreBlob())
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

        await using var dbPlugins = pluginDbContextFactory.CreateContext();
        var userInDb = dbPlugins.PayrollUsers.SingleOrDefault(a =>
            a.StoreId == storeId && a.Email == model.Email.ToLowerInvariant());

        if (userInDb != null)
        {
            // Block OneTime users from logging in
            if (userInDb.State == VendorPayUserState.OneTime)
            {
                ModelState.AddModelError(nameof(model.Password), "This account cannot log in. Please contact the administrator.");
                return View(model);
            }

            if (userInDb.State == VendorPayUserState.Active && hasher.IsValidPassword(userInDb, model.Password))
            {
                httpContextAccessor.HttpContext!.Session.SetString(VENDORPAY_AUTH_USER_ID, userInDb!.Id);
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
        await using var dbPlugins = pluginDbContextFactory.CreateContext();

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
        await using var dbPlugins = pluginDbContextFactory.CreateContext();

        var invitation =
            dbPlugins.PayrollInvitations.SingleOrDefault(i => i.Token == model.Token && !i.AcceptedAt.HasValue && i.CreatedAt.AddDays(7) >= DateTime.UtcNow);

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

        if (dbUser.State != VendorPayUserState.Pending)
        {
            ModelState.AddModelError(nameof(model.NewPassword),
                "User with the same email already exists. Kindly reach out to the BTCPay Server instance admin");
            return View(model);
        }

        if (!ModelState.IsValid)
            return View(model);

        dbUser.Password = hasher.HashPassword(invitation.Id, model.NewPassword);
        dbUser.State = VendorPayUserState.Active;
        invitation.AcceptedAt = DateTime.UtcNow;
        dbPlugins.Update(dbUser);
        dbPlugins.Update(invitation);
        await dbPlugins.SaveChangesAsync();
        TempData.SetStatusMessageModel(new StatusMessageModel { Message = "User created successfully", Severity = StatusMessageModel.StatusSeverity.Success });
        return RedirectToAction(nameof(Login), "Public", new { storeId = invitation.StoreId });
    }

    [HttpGet("logout")]
    public IActionResult Logout(string storeId)
    {
        httpContextAccessor.HttpContext?.Session.Remove(VENDORPAY_AUTH_USER_ID);
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

        await using var ctx = pluginDbContextFactory.CreateContext();
        var vendorPayInvoices = await ctx.PayrollInvoices
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
            Invoices = vendorPayInvoices.Select(tuple => new VendorPayInvoiceViewModel
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
            await using var dbPlugin = pluginDbContextFactory.CreateContext();
            userId = httpContextAccessor.HttpContext!.Session.GetString(VENDORPAY_AUTH_USER_ID);
            var userInDb = dbPlugin.PayrollUsers.SingleOrDefault(a =>
                a.StoreId == storeId && a.Id == userId && a.State == VendorPayUserState.Active);
            if (userInDb == null)
                return new StoreUserValidator { ErrorActionResult = redirectToLogin(storeId) };
            userId = userInDb.Id;
        }

        return new StoreUserValidator { Store = store, UserId = userId };
    }


    [HttpGet("DownloadInvoices")]
    public async Task<IActionResult> DownloadInvoices(string storeId, string invoiceId)
    {
        var vali = await validateStoreAndUser(storeId, true);
        if (vali.ErrorActionResult != null)
            return vali.ErrorActionResult;

        await using var ctx = pluginDbContextFactory.CreateContext();
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

        var settings = await pluginDbContextFactory.GetSettingAsync(storeId);
        var model = new PublicVendorPayInvoiceUploadViewModel
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
    public async Task<IActionResult> Upload(string storeId, PublicVendorPayInvoiceUploadViewModel model)
    {
        var vali = await validateStoreAndUser(storeId, true);
        if (vali.ErrorActionResult != null)
            return vali.ErrorActionResult;

        model.StoreId = vali.Store.Id;
        model.StoreName = vali.Store.StoreName;
        model.StoreBranding = await StoreBrandingViewModel.CreateAsync(Request, uriResolver, vali.Store.GetStoreBlob());

        var validation = await vendorPayInvoiceUploadHelper.Process(storeId, vali.UserId, model);
        if (!validation.IsValid)
        {
            validation.ApplyToModelState(ModelState);
            return View(model);
        }

        // Load the created invoice with user for email notification
        await using var ctx = pluginDbContextFactory.CreateContext();
        var createdInvoice = await ctx.PayrollInvoices
            .Include(i => i.User)
            .OrderByDescending(i => i.CreatedAt)
            .FirstOrDefaultAsync(i => i.UserId == vali.UserId);

        if (createdInvoice != null)
            await emailService.SendAdminNotificationOnInvoiceUpload(storeId, createdInvoice);

        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Message = "Invoice uploaded successfully", Severity = StatusMessageModel.StatusSeverity.Success
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

        await using var dbPlugins = pluginDbContextFactory.CreateContext();
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
        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Message = "Password successfully changed", Severity = StatusMessageModel.StatusSeverity.Success
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

        await using var ctx = pluginDbContextFactory.CreateContext();

        var invoice = ctx.PayrollInvoices.Include(c => c.User)
            .SingleOrDefault(a => a.Id == id);

        if (invoice == null)
            return NotFound();

        if (invoice.State != VendorPayInvoiceState.AwaitingApproval)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = "Invoice cannot be deleted as it has been actioned upon", Severity = StatusMessageModel.StatusSeverity.Error
            });
            return RedirectToAction(nameof(ListInvoices), new { storeId = vali.Store.Id });
        }

        return View("Confirm",
            new ConfirmModel("Delete Invoice", $"Do you really want to delete the invoice for {invoice.Amount} {invoice.Currency} from {invoice.User.Name}?",
                "Delete"));
    }

    [HttpPost("delete/{id}")]
    public async Task<IActionResult> DeletePost(string storeId, string id)
    {
        var vali = await validateStoreAndUser(storeId, true);
        if (vali.ErrorActionResult != null)
            return vali.ErrorActionResult;

        await using var ctx = pluginDbContextFactory.CreateContext();

        var invoice = ctx.PayrollInvoices
            .Include(i => i.User)
            .Single(a => a.Id == id);

        if (invoice.State != VendorPayInvoiceState.AwaitingApproval)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = "Invoice cannot be deleted as it has been actioned upon", Severity = StatusMessageModel.StatusSeverity.Error
            });
            return RedirectToAction(nameof(ListInvoices), new { storeId });
        }

        // Send admin notification before deleting
        var vendorName = invoice.User?.Name ?? "Unknown";
        var vendorEmail = invoice.User?.Email ?? "unknown";
        await emailService.SendAdminNotificationOnInvoiceDelete(storeId, invoice, vendorName, vendorEmail);

        ctx.Remove(invoice);
        await ctx.SaveChangesAsync();

        TempData.SetStatusMessageModel(
            new StatusMessageModel { Message = "Invoice deleted successfully", Severity = StatusMessageModel.StatusSeverity.Success });
        return RedirectToAction(nameof(ListInvoices), new { storeId });
    }

    [HttpGet("accountless-upload/{hash}")]
    public async Task<IActionResult> AccountlessUpload(string storeId, string hash)
    {
        await using var dbMain = dbContextFactory.CreateContext();
        var store = await dbMain.Stores.SingleOrDefaultAsync(a => a.Id == storeId);
        if (store == null)
            return NotFound();

        var settings = await pluginDbContextFactory.GetSettingAsync(storeId);

        if (!settings.AccountlessUploadEnabled)
            return NotFound();

        // Validate hash
        var expectedHash = GenerateUploadHash(storeId, settings.UploadCode);
        if (hash != expectedHash)
        {
            var errorModel = new BaseVendorPayPublicViewModel
            {
                StoreName = store.StoreName,
                StoreBranding = await StoreBrandingViewModel.CreateAsync(Request, uriResolver, store.GetStoreBlob())
            };
            return View("AccountlessUploadInvalidLink", errorModel);
        }

        var model = new AccountlessUploadViewModel
        {
            StoreId = store.Id,
            StoreName = store.StoreName,
            StoreBranding = await StoreBrandingViewModel.CreateAsync(Request, uriResolver, store.GetStoreBlob()),
            Amount = 0,
            Currency = store.GetStoreBlob().DefaultCurrency,
            PurchaseOrdersRequired = settings.PurchaseOrdersRequired,
            DescriptionTitle = settings.DescriptionTitle
        };

        return View(model);
    }

    [HttpPost("accountless-upload/{hash}")]
    public async Task<IActionResult> AccountlessUpload(string storeId, string hash, AccountlessUploadViewModel model)
    {
        await using var dbMain = dbContextFactory.CreateContext();
        var store = await dbMain.Stores.SingleOrDefaultAsync(a => a.Id == storeId);
        if (store == null)
            return NotFound();

        var settings = await pluginDbContextFactory.GetSettingAsync(storeId);

        if (!settings.AccountlessUploadEnabled)
            return NotFound();

        // Validate hash
        var expectedHash = GenerateUploadHash(storeId, settings.UploadCode);
        if (hash != expectedHash)
        {
            var errorModel = new BaseVendorPayPublicViewModel
            {
                StoreName = store.StoreName,
                StoreBranding = await StoreBrandingViewModel.CreateAsync(Request, uriResolver, store.GetStoreBlob())
            };
            return View("AccountlessUploadInvalidLink", errorModel);
        }

        model.StoreId = store.Id;
        model.StoreName = store.StoreName;
        model.StoreBranding = await StoreBrandingViewModel.CreateAsync(Request, uriResolver, store.GetStoreBlob());
        model.DescriptionTitle = settings.DescriptionTitle;

        if (model.UploadCode != settings.UploadCode)
        {
            ModelState.AddModelError(nameof(model.UploadCode), "Invalid Upload Code");
        }

        if (!string.IsNullOrEmpty(settings.DescriptionTitle) && string.IsNullOrWhiteSpace(model.Description))
        {
            ModelState.AddModelError(nameof(model.Description), "Description is required");
        }

        if (!ModelState.IsValid)
            return View(model);

        await using var dbPlugin = pluginDbContextFactory.CreateContext();
        
        var existingUser = dbPlugin.PayrollUsers.SingleOrDefault(u => 
            u.StoreId == storeId && u.Email == model.Email.ToLowerInvariant());

        string userId;
        if (existingUser != null)
        {
            // Security check: Block if user has Active or Pending state (real user account)
            if (existingUser.State == VendorPayUserState.Active || existingUser.State == VendorPayUserState.Pending)
            {
                ModelState.AddModelError(nameof(model.Email), 
                    "This email is registered. Please log in to upload invoices.");
                return View(model);
            }

            // Allow reuse for OneTime or Disabled users
            userId = existingUser.Id;
            existingUser.Name = model.Name;
            existingUser.State = VendorPayUserState.OneTime;
            dbPlugin.Update(existingUser);
            await dbPlugin.SaveChangesAsync();
        }
        else
        {
            var newUser = new PayrollUser
            {
                StoreId = storeId,
                Name = model.Name,
                Email = model.Email.ToLowerInvariant(),
                State = VendorPayUserState.OneTime,
                Password = hasher.HashPassword(Guid.NewGuid().ToString(), Guid.NewGuid().ToString())
            };
            dbPlugin.Add(newUser);
            await dbPlugin.SaveChangesAsync();
            userId = newUser.Id;
        }

        var uploadModel = new PublicVendorPayInvoiceUploadViewModel
        {
            Amount = model.Amount,
            Currency = model.Currency,
            Destination = model.Destination,
            Description = model.Description,
            Invoice = model.Invoice,
            PurchaseOrder = model.PurchaseOrder,
            ExtraFiles = model.ExtraFiles
        };

        var validation = await vendorPayInvoiceUploadHelper.Process(storeId, userId, uploadModel);
        if (!validation.IsValid)
        {
            validation.ApplyToModelState(ModelState);
            return View(model);
        }

        var createdInvoice = await dbPlugin.PayrollInvoices
            .Include(i => i.User)
            .OrderByDescending(i => i.CreatedAt)
            .FirstOrDefaultAsync(i => i.UserId == userId);

        if (createdInvoice != null)
        {
            await emailService.SendAdminNotificationOnInvoiceUpload(storeId, createdInvoice);

            // Send account conversion email if enabled
            if (settings.AllowOneTimeAccountConversion)
            {
                var user = await dbPlugin.PayrollUsers.FindAsync(userId);
                if (user != null && user.State == VendorPayUserState.OneTime)
                {
                    // Create invitation for account conversion
                    var invitation = new PayrollInvitation
                    {
                        StoreId = storeId,
                        Email = user.Email,
                        Token = Guid.NewGuid().ToString(),
                        CreatedAt = DateTime.UtcNow
                    };
                    dbPlugin.Add(invitation);
                    await dbPlugin.SaveChangesAsync();

                    var registrationLink = Url.Action(
                        "AcceptInvitation",
                        "Public",
                        new { storeId, token = invitation.Token },
                        HttpContext.Request.Scheme,
                        HttpContext.Request.Host.Value);

                    await emailService.SendOneTimeAccountConversionEmail(storeId, user, registrationLink);
                }
            }
        }

        return View("AccountlessUploadSuccess", model);
    }

    private class StoreUserValidator
    {
        public IActionResult ErrorActionResult { get; set; }
        public StoreData Store { get; set; }
        public string UserId { get; set; }
    }
}
