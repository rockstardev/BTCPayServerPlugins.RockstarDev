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
    [IgnoreAntiforgeryToken]
    [AllowAnonymous]
    public async Task<IActionResult> MarkSent(string storeId, string invoiceId, string? customerNote, string? methodId)
    {
        if (Request.Headers["X-Requested-With"] != "RockstarHttpRequester")
            return BadRequest();

        var method = await configService.GetEnabledMethodOptions(storeId).ContinueWith(t => t.Result.FirstOrDefault(m => m.MethodId == methodId));
        if (method is null)
            return BadRequest("Method not found.");

        var existing = await paymentsService.GetByInvoiceId(storeId, invoiceId);
        if (existing is null)
            existing = await paymentsService.RecordMethodSelected(storeId, invoiceId, method);

        await paymentsService.CustomerMarkSent(storeId, invoiceId, null, null);
        return Json(new { success = true });
    }
}
