using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.RockstarDev.Plugins.OfflinePayments.Services;
using BTCPayServer.RockstarDev.Plugins.OfflinePayments.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.RockstarDev.Plugins.OfflinePayments.Controllers;

[Route("~/plugins/{storeId}/offline-payments/")]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanModifyStoreSettings)]
public class OfflinePaymentsPublicController(OfflineMethodConfigService configService, OfflinePaymentsService paymentsService) : Controller
{
    [HttpGet("{invoiceId}/{methodId}")]
    public async Task<IActionResult> Instructions(string storeId, string invoiceId, string methodId)
    {
        var methods = await configService.GetEnabledMethodOptions(storeId);
        var method = methods.FirstOrDefault(m => string.Equals(m.MethodId, methodId, StringComparison.OrdinalIgnoreCase));
        if (method is null)
            return NotFound();

        var existing = await paymentsService.GetByInvoiceId(storeId, invoiceId);
        if (existing is null)
        {
            existing = await paymentsService.RecordMethodSelected(storeId, invoiceId, method);
        }
        await paymentsService.MarkInstructionsAsViewed(existing.Id);
        var vm = new OfflineCheckoutViewModel
        {
            InvoiceId = invoiceId,
            StoreId = storeId,
            Method = method,
            ResolvedReference = existing.ResolvedReference ?? invoiceId,
            PendingPaymentId = existing.Id,
            AlreadyMarkedSent = existing.CustomerMarkedSentAt.HasValue
        };
        return View(vm);
    }

    [HttpPost("{invoiceId}/mark-sent")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkSent(string storeId, string invoiceId, string? customerNote, string? methodId)
    {
        var result = await paymentsService.CustomerMarkSent(storeId, invoiceId, customerNote, null);
        if (result is null)
            return BadRequest("Payment record not found.");

        return RedirectToAction(nameof(Instructions), new { storeId, invoiceId, methodId });
    }
}
