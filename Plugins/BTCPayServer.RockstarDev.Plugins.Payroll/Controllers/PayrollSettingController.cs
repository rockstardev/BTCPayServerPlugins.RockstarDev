using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using BTCPayServer.RockstarDev.Plugins.Payroll.Logic;
using BTCPayServer.RockstarDev.Plugins.Payroll.ViewModels;
using BTCPayServer.Services;
using System;
using BTCPayServer.Abstractions.Extensions;
using Microsoft.AspNetCore.Routing;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Controllers;

[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class PayrollSettingController(PayrollPluginDbContextFactory payrollPluginDbContextFactory, 
    PoliciesSettings policiesSettings, LinkGenerator linkGenerator) : Controller
{
    private StoreData CurrentStore => HttpContext.GetStoreData();
    private const string DefaultEmailTemplate = @"Hello {Name},

Your invoice submitted on {CreatedDate} has been paid on {DatePaid}.

See all your invoices on: {VendorPayPublicLink}

Thank you,  
{StoreName}";


    [HttpGet("~/plugins/{storeId}/payroll/settings")]
    public async Task<IActionResult> Settings(string storeId)
    {
        var settings = await payrollPluginDbContextFactory.GetSettingAsync(storeId);
        var model = new PayrollSettingViewModel
        {
            EmailVendorOnInvoicePaid = settings.EmailVendorOnInvoicePaid,
            EmailTemplate = settings.EmailTemplate ?? DefaultEmailTemplate,
            MakeInvoiceFileOptional = settings.MakeInvoiceFilesOptional,
            PurchaseOrdersRequired = settings.PurchaseOrdersRequired
        };
        ViewData["RequiresConfirmedEmail"] = policiesSettings.RequiresConfirmedEmail;
        return View(model);
    }

    [HttpPost("~/plugins/{storeId}/payroll/settings")]

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
            EmailTemplate = model.EmailTemplate,
            EmailVendorOnInvoicePaid = model.EmailVendorOnInvoicePaid,
            MakeInvoiceFilesOptional = model.MakeInvoiceFileOptional,
            PurchaseOrdersRequired = model.PurchaseOrdersRequired,
            VendorPayPublicLink = link
        };
        
        await payrollPluginDbContextFactory.SetSettingAsync(storeId, settings);
        return RedirectToAction(nameof(PayrollInvoiceController.List), "PayrollInvoice", new { storeId = CurrentStore.Id });

    }
}