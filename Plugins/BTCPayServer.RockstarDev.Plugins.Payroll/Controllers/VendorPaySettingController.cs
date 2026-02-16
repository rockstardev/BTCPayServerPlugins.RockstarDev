using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Data;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Data;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Logic;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Security;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Services;
using BTCPayServer.RockstarDev.Plugins.VendorPay.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using MimeKit;

namespace BTCPayServer.RockstarDev.Plugins.VendorPay.Controllers;

[Route("~/plugins/{storeId}/vendorpay/", Order = 0)]
[Route("~/plugins/{storeId}/payroll/", Order = 1)]
[Authorize(Policy = VendorPayPermissions.SettingsManage, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class VendorPaySettingController(
    PluginDbContextFactory pluginDbContextFactory,
    EmailService emailService,
    LinkGenerator linkGenerator) : Controller

{
    private StoreData CurrentStore => HttpContext.GetStoreData();

    [HttpGet("settings")]
    public async Task<IActionResult> Settings(string storeId)
    {
        var settings = await pluginDbContextFactory.GetSettingAsync(storeId);
        var model = new VendorPaySettingViewModel
        {
            MakeInvoiceFileOptional = settings.MakeInvoiceFilesOptional,
            PurchaseOrdersRequired = settings.PurchaseOrdersRequired,
            EmailOnInvoicePaid = settings.EmailOnInvoicePaid,
            InvoiceFiatConversionAdjustment = settings.InvoiceFiatConversionAdjustment,
            InvoiceFiatConversionAdjustmentPercentage = settings.InvoiceFiatConversionAdjustmentPercentage,
            UserInviteEmailSubject = settings.UserInviteEmailSubject ?? VendorPaySettingViewModel.Defaults.UserInviteEmailSubject,
            UserInviteEmailBody = settings.UserInviteEmailBody ?? VendorPaySettingViewModel.Defaults.UserInviteEmailBody,
            EmailOnInvoicePaidSubject = settings.EmailOnInvoicePaidSubject ?? VendorPaySettingViewModel.Defaults.EmailOnInvoicePaidSubject,
            EmailOnInvoicePaidBody = settings.EmailOnInvoicePaidBody ?? VendorPaySettingViewModel.Defaults.EmailOnInvoicePaidBody,
            EmailReminders = settings.EmailReminders,
            EmailRemindersSubject = settings.EmailRemindersSubject ?? VendorPaySettingViewModel.Defaults.EmailRemindersSubject,
            EmailRemindersBody = settings.EmailRemindersBody ?? VendorPaySettingViewModel.Defaults.EmailRemindersBody,
            EmailAdminOnInvoiceUploaded = settings.EmailAdminOnInvoiceUploaded,
            EmailAdminOnInvoiceUploadedAddress = settings.EmailAdminOnInvoiceUploadedAddress,
            EmailAdminOnInvoiceUploadedSubject =
                settings.EmailAdminOnInvoiceUploadedSubject ?? VendorPaySettingViewModel.Defaults.EmailAdminOnInvoiceUploadedSubject,
            EmailAdminOnInvoiceUploadedBody =
                settings.EmailAdminOnInvoiceUploadedBody ?? VendorPaySettingViewModel.Defaults.EmailAdminOnInvoiceUploadedBody,
            EmailAdminOnInvoiceDeleted = settings.EmailAdminOnInvoiceDeleted,
            EmailAdminOnInvoiceDeletedAddress = settings.EmailAdminOnInvoiceDeletedAddress,
            EmailAdminOnInvoiceDeletedSubject =
                settings.EmailAdminOnInvoiceDeletedSubject ?? VendorPaySettingViewModel.Defaults.EmailAdminOnInvoiceDeletedSubject,
            EmailAdminOnInvoiceDeletedBody = settings.EmailAdminOnInvoiceDeletedBody ?? VendorPaySettingViewModel.Defaults.EmailAdminOnInvoiceDeletedBody,
            EmailUploaderOnInvoiceUploaded = settings.EmailUploaderOnInvoiceUploaded,
            EmailUploaderOnInvoiceUploadedSubject =
                settings.EmailUploaderOnInvoiceUploadedSubject ?? VendorPaySettingViewModel.Defaults.EmailUploaderOnInvoiceUploadedSubject,
            EmailUploaderOnInvoiceUploadedBody =
                settings.EmailUploaderOnInvoiceUploadedBody ?? VendorPaySettingViewModel.Defaults.EmailUploaderOnInvoiceUploadedBody,
            AccountlessUploadEnabled = settings.AccountlessUploadEnabled,
            UploadCode = settings.UploadCode,
            DescriptionTitle = settings.DescriptionTitle
        };

        ViewData["StoreEmailSettingsConfigured"] = await emailService.IsEmailSettingsConfigured(storeId);
        return View(model);
    }

    [HttpPost("settings")]
    public async Task<IActionResult> Settings(string storeId, VendorPaySettingViewModel model)
    {
        if (CurrentStore is null)
            return NotFound();

        if (model.EmailReminders && string.IsNullOrEmpty(model.EmailRemindersSubject))
            ModelState.AddModelError(nameof(model.EmailRemindersSubject), "Value cannot be empty. Kindly include an email subject");

        if (model.EmailReminders && string.IsNullOrEmpty(model.EmailRemindersBody))
            ModelState.AddModelError(nameof(model.EmailRemindersBody), "Value cannot be empty. Kindly include an email body");

        if (model.EmailAdminOnInvoiceUploaded && string.IsNullOrEmpty(model.EmailAdminOnInvoiceUploadedAddress))
            ModelState.AddModelError(nameof(model.EmailAdminOnInvoiceUploadedAddress), "Admin email address is required when notifications are enabled");

        if (model.EmailAdminOnInvoiceUploaded && !string.IsNullOrEmpty(model.EmailAdminOnInvoiceUploadedAddress))
            if (!ValidateEmailAddressList(model.EmailAdminOnInvoiceUploadedAddress))
                ModelState.AddModelError(nameof(model.EmailAdminOnInvoiceUploadedAddress),
                    "Invalid email address format. Use comma-separated email addresses.");

        if (model.EmailAdminOnInvoiceDeleted && string.IsNullOrEmpty(model.EmailAdminOnInvoiceDeletedAddress))
            ModelState.AddModelError(nameof(model.EmailAdminOnInvoiceDeletedAddress), "Admin email address is required when notifications are enabled");

        if (model.EmailAdminOnInvoiceDeleted && !string.IsNullOrEmpty(model.EmailAdminOnInvoiceDeletedAddress))
            if (!ValidateEmailAddressList(model.EmailAdminOnInvoiceDeletedAddress))
                ModelState.AddModelError(nameof(model.EmailAdminOnInvoiceDeletedAddress), "Invalid email address format. Use comma-separated email addresses.");

        if (model.AccountlessUploadEnabled && string.IsNullOrEmpty(model.UploadCode))
            ModelState.AddModelError(nameof(model.UploadCode), "Upload Code is required");

        if (string.IsNullOrWhiteSpace(model.UserInviteEmailSubject))
            ModelState.AddModelError(nameof(model.UserInviteEmailSubject), "Invite email subject cannot be empty");

        if (string.IsNullOrWhiteSpace(model.UserInviteEmailBody))
            ModelState.AddModelError(nameof(model.UserInviteEmailBody), "Invite email template cannot be empty");

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
        var settings = new VendorPayStoreSetting
        {
            EmailReminders = model.EmailReminders,
            EmailRemindersBody = model.EmailRemindersBody,
            EmailRemindersSubject = model.EmailRemindersSubject,
            MakeInvoiceFilesOptional = model.MakeInvoiceFileOptional,
            PurchaseOrdersRequired = model.PurchaseOrdersRequired,
            EmailOnInvoicePaid = model.EmailOnInvoicePaid,
            EmailOnInvoicePaidSubject = model.EmailOnInvoicePaidSubject,
            EmailOnInvoicePaidBody = model.EmailOnInvoicePaidBody,
            UserInviteEmailBody = model.UserInviteEmailBody,
            UserInviteEmailSubject = model.UserInviteEmailSubject,
            InvoiceFiatConversionAdjustment = model.InvoiceFiatConversionAdjustment,
            InvoiceFiatConversionAdjustmentPercentage = model.InvoiceFiatConversionAdjustmentPercentage,
            VendorPayPublicLink = link,
            EmailAdminOnInvoiceUploaded = model.EmailAdminOnInvoiceUploaded,
            EmailAdminOnInvoiceUploadedAddress = model.EmailAdminOnInvoiceUploadedAddress,
            EmailAdminOnInvoiceUploadedSubject = model.EmailAdminOnInvoiceUploadedSubject,
            EmailAdminOnInvoiceUploadedBody = model.EmailAdminOnInvoiceUploadedBody,
            EmailAdminOnInvoiceDeleted = model.EmailAdminOnInvoiceDeleted,
            EmailAdminOnInvoiceDeletedAddress = model.EmailAdminOnInvoiceDeletedAddress,
            EmailAdminOnInvoiceDeletedSubject = model.EmailAdminOnInvoiceDeletedSubject,
            EmailAdminOnInvoiceDeletedBody = model.EmailAdminOnInvoiceDeletedBody,
            EmailUploaderOnInvoiceUploaded = model.EmailUploaderOnInvoiceUploaded,
            EmailUploaderOnInvoiceUploadedSubject = model.EmailUploaderOnInvoiceUploadedSubject,
            EmailUploaderOnInvoiceUploadedBody = model.EmailUploaderOnInvoiceUploadedBody,
            AccountlessUploadEnabled = model.AccountlessUploadEnabled,
            UploadCode = model.UploadCode,
            DescriptionTitle = model.DescriptionTitle
        };


        await pluginDbContextFactory.SetSettingAsync(storeId, settings);
        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Message = "Vendor pay settings updated successfully", Severity = StatusMessageModel.StatusSeverity.Success
        });
        return RedirectToAction(nameof(Settings), new { storeId });
    }

    private bool ValidateEmailAddressList(string emailList)
    {
        if (string.IsNullOrWhiteSpace(emailList))
            return false;

        var emails = emailList.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.Trim())
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .ToList();

        if (!emails.Any())
            return false;

        // Validate each email address using MimeKit's InternetAddress.TryParse
        return emails.All(email => InternetAddress.TryParse(email, out _));
    }
}
