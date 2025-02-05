using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using BTCPayServer.RockstarDev.Plugins.Payroll.Logic;
using BTCPayServer.RockstarDev.Plugins.Payroll.ViewModels;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Services.Mails;
using Microsoft.AspNetCore.Routing;
using BTCPayServer.Abstractions.Models;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Controllers;

[Route("~/plugins/{storeId}/payroll/")]
[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class PayrollSettingController(PayrollPluginDbContextFactory payrollPluginDbContextFactory, 
    EmailSenderFactory emailSenderFactory, LinkGenerator linkGenerator) : Controller
{
    private StoreData CurrentStore => HttpContext.GetStoreData();

    private const string DefaultEmailOnInvoicePaidSubject = @"Your invoice has been paid";
    private const string DefaultEmailOnInvoicePaidBody = @"Hello {Name},

Your invoice submitted on {CreatedAt} has been paid on {PaidAt}.

See all your invoices on: {VendorPayPublicLink}

Thank you,  
{StoreName}";


    [HttpGet("settings")]
    public async Task<IActionResult> Settings(string storeId)
    {
        var settings = await payrollPluginDbContextFactory.GetSettingAsync(storeId);
        var model = new PayrollSettingViewModel
        {
            MakeInvoiceFileOptional = settings.MakeInvoiceFilesOptional,
            PurchaseOrdersRequired = settings.PurchaseOrdersRequired,
            EmailOnInvoicePaid = settings.EmailOnInvoicePaid,
            EmailOnInvoicePaidSubject = settings.EmailOnInvoicePaidSubject ?? DefaultEmailOnInvoicePaidSubject,
            EmailOnInvoicePaidBody = settings.EmailOnInvoicePaidBody ?? DefaultEmailOnInvoicePaidBody,
            EmailInviteForUsers = settings.EmailInviteForUsers
        };
        var emailSender = await emailSenderFactory.GetEmailSender(storeId);
        ViewData["StoreEmailSettingsConfigured"] = (await emailSender.GetEmailSettings() ?? new EmailSettings()).IsComplete();
        return View(model);
    }

    [HttpPost("settings")]

    public async Task<IActionResult> Settings(string storeId, PayrollSettingViewModel model)
    {
        if (CurrentStore is null)
            return NotFound();

        var link = linkGenerator.GetUriByAction(
            action: "ListInvoices",
            controller: "Public",
            values: new { storeId },
            scheme: "https",
            host: HttpContext.Request.Host);
        var settings = new PayrollStoreSetting
        {
            MakeInvoiceFilesOptional = model.MakeInvoiceFileOptional,
            PurchaseOrdersRequired = model.PurchaseOrdersRequired,
            EmailOnInvoicePaid = model.EmailOnInvoicePaid,
            EmailOnInvoicePaidSubject = model.EmailOnInvoicePaidSubject,
            EmailOnInvoicePaidBody = model.EmailOnInvoicePaidBody,
            EmailInviteForUsers = model.EmailInviteForUsers,
            VendorPayPublicLink = link
        };
        
        await payrollPluginDbContextFactory.SetSettingAsync(storeId, settings);
        TempData.SetStatusMessageModel(new StatusMessageModel()
        {
            Message = $"Vendor pay settings updated successfully",
            Severity = StatusMessageModel.StatusSeverity.Success
        });
        return RedirectToAction(nameof(PayrollInvoiceController.List), "PayrollInvoice", new { storeId = CurrentStore.Id });

    }
}