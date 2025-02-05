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
    private const string DefaultUserInviteEmailSubject = @"You are invited to create a Vendor Pay account";
    private const string DefaultEmailOnInvoicePaidBody = @"Hello {Name},

Your invoice submitted on {CreatedAt} has been paid on {PaidAt}.

See all your invoices on: {VendorPayPublicLink}

Thank you,  
{StoreName}";
    private const string DefaultUserInviteEmailBody = @"Hello {Name},

You are invited to create an account on {StoreName}'s Vendor Pay portal by visiting the following link:  
{VendorPayRegisterLink}

Once your account is created and you log in, you will be able to:
- View your invoices and submit new ones.
- Click 'Upload Invoice' to add a payable invoice. Fill out the information accurately. By using the Vendor Pay portal, you are solely responsible for providing an accurate Bitcoin address and assume all liability for any incorrect or inaccessible address.
- Describe what the payment is related to; be as descriptive as possible to avoid delays.
- Upload the corresponding invoice file.

Payments will be issued in accordance with the terms of the contracted payment and purchase order.

If you have any questions, please reach out to XXXXXX.

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