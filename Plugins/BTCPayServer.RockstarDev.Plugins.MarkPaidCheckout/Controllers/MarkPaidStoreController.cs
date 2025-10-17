using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Payments;
using BTCPayServer.RockstarDev.Plugins.MarkPaidCheckout.PaymentHandlers;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.RockstarDev.Plugins.MarkPaidCheckout.Controllers;

[Route("stores/{storeId}/markpaid")]
[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class MarkPaidStoreController(
    StoreRepository storeRepository,
    InvoiceRepository invoiceRepository,
    PaymentMethodHandlerDictionary handlers,
    PaymentService paymentService,
    MarkPaidMethodsRegistry registry,
    EventAggregator eventAggregator) : Controller
{
    private StoreData StoreData => HttpContext.GetStoreData();

    [HttpGet("method/{method}")]
    public IActionResult MethodConfig(string method)
    {
        if (string.IsNullOrWhiteSpace(method) || !registry.Methods.Contains(method, StringComparer.OrdinalIgnoreCase))
            return NotFound();

        var store = StoreData;
        var blob = store.GetStoreBlob();
        var pmid = new PaymentMethodId(method);
        var hasConfig = store.GetPaymentMethodConfig(pmid, handlers) is not null;
        var enabled = hasConfig && !blob.GetExcludedPaymentMethods().Match(pmid);
        var vm = new MethodConfigVm { Method = pmid.ToString(), Enabled = enabled };
        return View("Views/MarkPaid/MethodConfig", vm);
    }

    [HttpPost("method/{method}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MethodConfig(string method, MethodConfigVm vm)
    {
        if (string.IsNullOrWhiteSpace(method) || !registry.Methods.Contains(method, StringComparer.OrdinalIgnoreCase))
            return NotFound();

        var store = StoreData;
        var blob = store.GetStoreBlob();
        var pmid = new PaymentMethodId(method);
        blob.SetExcluded(pmid, !vm.Enabled);
        if (vm.Enabled) EnsureConfigEntry(store, pmid);
        store.SetStoreBlob(blob);
        await storeRepository.UpdateStore(store);
        return RedirectToAction(nameof(MethodConfig), new { storeId = store.Id, method });
    }

    private void EnsureConfigEntry(StoreData store, PaymentMethodId pmid)
    {
        // Ensure there is a config entry so BTCPay counts this method as available
        var existing = store.GetPaymentMethodConfig(pmid, handlers);
        if (existing is null) store.SetPaymentMethodConfig(handlers[pmid], new MarkPaidPaymentMethodConfig());
    }

    [HttpPost("MarkAsPaid")]
    [AllowAnonymous]
    public async Task<IActionResult> MarkAsPaid(string invoiceId, string storeId, string returnUrl, string method)
    {
        if (Request.Headers["X-Requested-With"] != "RockstarHttpRequester")
            return BadRequest();

        var invoice = await invoiceRepository.GetInvoice(invoiceId, true);
        if (invoice.StoreId != storeId || invoice.Status != InvoiceStatus.New)
            return Json(new { success = false, error = "Invoice not found or already paid" });

        var pmid = new PaymentMethodId(method);
        var handler = handlers[pmid];
        var paymentData = new PaymentData
        {
            Id = Guid.NewGuid().ToString(),
            Created = DateTimeOffset.UtcNow,
            Status = PaymentStatus.Settled,
            Currency = invoice.Currency,
            InvoiceDataId = invoiceId,
            Amount = invoice.Price,
            PaymentMethodId = handler.PaymentMethodId.ToString()
        }.Set(invoice, handler, new object());
        var payment = await paymentService.AddPayment(paymentData);
        
        if (payment != null)
        {
            await invoiceRepository.MarkInvoiceStatus(invoice.Id, InvoiceStatus.Settled);
        }

        return Json(new { success = true, status = invoice.Status.ToString() });
    }

    public class MethodConfigVm
    {
        public string Method { get; set; } = string.Empty;
        public bool Enabled { get; set; }
    }
}
