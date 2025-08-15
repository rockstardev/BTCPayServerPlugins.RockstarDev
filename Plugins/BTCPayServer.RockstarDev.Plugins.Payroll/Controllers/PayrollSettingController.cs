using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data;
using BTCPayServer.RockstarDev.Plugins.Payroll.Logic;
using BTCPayServer.RockstarDev.Plugins.Payroll.Services;
using BTCPayServer.RockstarDev.Plugins.Payroll.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Controllers;

[Route("~/plugins/{storeId}/vendorpay/", Order = 0)]
[Route("~/plugins/{storeId}/payroll/", Order = 1)]
[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class PayrollSettingController(
    PluginDbContextFactory pluginDbContextFactory,
    EmailService emailService,
    LinkGenerator linkGenerator) : Controller

{
    private StoreData CurrentStore => HttpContext.GetStoreData();

    [HttpGet("settings")]
    public async Task<IActionResult> Settings(string storeId)
    {
        var settings = await pluginDbContextFactory.GetSettingAsync(storeId);
        var model = new PayrollSettingViewModel
        {
            MakeInvoiceFileOptional = settings.MakeInvoiceFilesOptional,
            PurchaseOrdersRequired = settings.PurchaseOrdersRequired,
            EmailOnInvoicePaid = settings.EmailOnInvoicePaid,
            InvoiceFiatConversionAdjustment = settings.InvoiceFiatConversionAdjustment,
            InvoiceFiatConversionAdjustmentPercentage = settings.InvoiceFiatConversionAdjustmentPercentage,
            EmailOnInvoicePaidSubject = settings.EmailOnInvoicePaidSubject ?? PayrollSettingViewModel.Defaults.EmailOnInvoicePaidSubject,
            EmailOnInvoicePaidBody = settings.EmailOnInvoicePaidBody ?? PayrollSettingViewModel.Defaults.EmailOnInvoicePaidBody,
            EmailReminders = settings.EmailReminders,
            EmailRemindersSubject = settings.EmailRemindersSubject ?? PayrollSettingViewModel.Defaults.EmailRemindersSubject,
            EmailRemindersBody = settings.EmailRemindersBody ?? PayrollSettingViewModel.Defaults.EmailRemindersBody
        };

        ViewData["StoreEmailSettingsConfigured"] = await emailService.IsEmailSettingsConfigured(storeId);
        return View(model);
    }

    [HttpPost("settings")]
    public async Task<IActionResult> Settings(string storeId, PayrollSettingViewModel model)
    {
        if (CurrentStore is null)
            return NotFound();

        if (model.EmailReminders && string.IsNullOrEmpty(model.EmailRemindersSubject))
            ModelState.AddModelError(nameof(model.EmailRemindersSubject), "Value cannot be empty. Kindly include an email subject");

        if (model.EmailReminders && string.IsNullOrEmpty(model.EmailRemindersBody))
            ModelState.AddModelError(nameof(model.EmailRemindersBody), "Value cannot be empty. Kindly include an email body");

        if (!ModelState.IsValid)
        {
            ViewData["StoreEmailSettingsConfigured"] = await emailService.IsEmailSettingsConfigured(storeId);
            return View(model);
        }

        var link = linkGenerator.GetUriByAction(
            "ListInvoices",
            "Public",
            new { storeId },
            "https",
            HttpContext.Request.Host);
        var settings = new PayrollStoreSetting
        {
            EmailReminders = model.EmailReminders,
            EmailRemindersBody = model.EmailRemindersBody,
            EmailRemindersSubject = model.EmailRemindersSubject,
            MakeInvoiceFilesOptional = model.MakeInvoiceFileOptional,
            PurchaseOrdersRequired = model.PurchaseOrdersRequired,
            EmailOnInvoicePaid = model.EmailOnInvoicePaid,
            EmailOnInvoicePaidSubject = model.EmailOnInvoicePaidSubject,
            EmailOnInvoicePaidBody = model.EmailOnInvoicePaidBody,
            InvoiceFiatConversionAdjustment = model.InvoiceFiatConversionAdjustment,
            InvoiceFiatConversionAdjustmentPercentage = model.InvoiceFiatConversionAdjustmentPercentage,
            VendorPayPublicLink = link
        };


        await pluginDbContextFactory.SetSettingAsync(storeId, settings);
        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Message = "Vendor pay settings updated successfully", Severity = StatusMessageModel.StatusSeverity.Success
        });
        return RedirectToAction(nameof(PayrollInvoiceController.List), "PayrollInvoice", new { storeId = CurrentStore.Id });
    }
}
