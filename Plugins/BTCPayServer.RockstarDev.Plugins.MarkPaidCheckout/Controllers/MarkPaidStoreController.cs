using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.RockstarDev.Plugins.MarkPaidCheckout.PaymentHandlers;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InvoiceData = BTCPayServer.Client.Models.InvoiceData;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.RockstarDev.Plugins.MarkPaidCheckout.Controllers;

[Route("stores/{storeId}/markpaid")]
[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class MarkPaidStoreController(
    StoreRepository storeRepository,
    InvoiceRepository invoiceRepository,
    PaymentMethodHandlerDictionary handlers,
    PaymentService paymentService,
    MarkPaidMethodsRegistry registry) : Controller
{
    private StoreData StoreData => HttpContext.GetStoreData();

    public class StoreMethodItem
    {
        public string Method { get; set; } = string.Empty;
        public bool Enabled { get; set; }
    }

    public class StoreConfigVm
    {
        public List<StoreMethodItem> Methods { get; set; } = new();
    }

    public class MethodConfigVm
    {
        public string Method { get; set; } = string.Empty;
        public bool Enabled { get; set; }
    }

    [HttpGet]
    public IActionResult StoreConfig()
    {
        var store = StoreData;
        var blob = store.GetStoreBlob();
        var vm = new StoreConfigVm();
        foreach (var m in registry.Methods)
        {
            var pmid = new PaymentMethodId(m);
            var enabled = !blob.GetExcludedPaymentMethods().Match(pmid);
            vm.Methods.Add(new StoreMethodItem { Method = m, Enabled = enabled });
        }

        return View("Views/MarkPaid/StoreConfig", vm);
    }

    [HttpPost]
    public async Task<IActionResult> StoreConfig(StoreConfigVm vm)
    {
        var store = StoreData;
        var blob = store.GetStoreBlob();
        var methodsSet = new HashSet<string>(vm.Methods?.Where(x => x.Enabled).Select(x => x.Method) ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
        foreach (var m in registry.Methods)
        {
            var pmid = new PaymentMethodId(m);
            var enabled = methodsSet.Contains(m);
            blob.SetExcluded(pmid, !enabled);
            if (enabled)
            {
                EnsureConfigEntry(store, pmid);
            }
        }

        store.SetStoreBlob(blob);
        await storeRepository.UpdateStore(store);
        return RedirectToAction(nameof(StoreConfig), new { storeId = store.Id });
    }

    [HttpGet("method/{method}")]
    public IActionResult MethodConfig(string method)
    {
        if (string.IsNullOrWhiteSpace(method) || !registry.Methods.Contains(method, StringComparer.OrdinalIgnoreCase))
            return NotFound();

        var store = StoreData;
        var blob = store.GetStoreBlob();
        var pmid = new PaymentMethodId(method);
        var enabled = !blob.GetExcludedPaymentMethods().Match(pmid);
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
        if (vm.Enabled)
        {
            EnsureConfigEntry(store, pmid);
        }
        store.SetStoreBlob(blob);
        await storeRepository.UpdateStore(store);
        return RedirectToAction(nameof(MethodConfig), new { storeId = store.Id, method });
    }

    private void EnsureConfigEntry(StoreData store, PaymentMethodId pmid)
    {
        // Ensure there is a config entry so BTCPay counts this method as available
        var existing = store.GetPaymentMethodConfig(pmid, handlers);
        if (existing is null)
        {
            store.SetPaymentMethodConfig(handlers[pmid], new MarkPaidPaymentMethodConfig());
        }
    }

    [HttpGet("MarkAsPaid")]
    [AllowAnonymous]
    public async Task<IActionResult> MarkAsPaid(string invoiceId, string storeId, string returnUrl, string method)
    {
        var invoice = await invoiceRepository.GetInvoice(invoiceId, true);
        if (invoice.StoreId != storeId || invoice.Status != InvoiceStatus.New)
            return Redirect(returnUrl);
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

        return Redirect(returnUrl);
    }
}
