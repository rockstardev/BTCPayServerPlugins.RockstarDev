using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client.Models;
using BTCPayServer.RockstarDev.Plugins.OfflinePayments.Services;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.RockstarDev.Plugins.OfflinePayments.Controllers;

[Route("~/plugins/{storeId}/offline-payments/")]
public class OfflinePaymentsPublicController(OfflineMethodConfigService configService, OfflinePaymentsService paymentsService,
    InvoiceRepository invoiceRepository, IFileService fileService) : Controller
{

    [HttpPost("{invoiceId}/mark-sent")]
    [IgnoreAntiforgeryToken]
    [AllowAnonymous]
    public async Task<IActionResult> MarkSent(string storeId, string invoiceId, string? customerNote, string? methodId, IFormFile? remittanceFile)
    {
        if (Request.Headers["X-Requested-With"] != "RockstarHttpRequester")
            return BadRequest();

        var invoice = await invoiceRepository.GetInvoice(invoiceId);
        if (invoice is null || invoice.StoreId != storeId)
            return BadRequest("Invoice not found.");

        if (invoice.Status != InvoiceStatus.New && invoice.Status != InvoiceStatus.Processing)
            return BadRequest("Invoice is not in a payable state.");

        var methods = await configService.GetEnabledMethodOptions(storeId);
        var method = methods.FirstOrDefault(m => m.MethodId == methodId);
        if (method is null)
            return BadRequest("Method not found.");

        string? remittanceFileId = null;
        if (remittanceFile is { Length: > 0 })
        {
            const long maxFileSize = 1_000_000;
            if (remittanceFile.Length > maxFileSize)
                return BadRequest($"Receipt file must be under {maxFileSize / 1_000_000}MB.");

            var uploaded = await fileService.AddFile(remittanceFile, method.UserId);
            remittanceFileId = uploaded?.Id;
        }
        var existing = await paymentsService.GetByInvoiceId(storeId, invoiceId);
        if (existing is null)
        {
            await paymentsService.RecordPayment(storeId, invoiceId, method, customerNote, remittanceFileId);
        }
        return Json(new { success = true });
    }
}
