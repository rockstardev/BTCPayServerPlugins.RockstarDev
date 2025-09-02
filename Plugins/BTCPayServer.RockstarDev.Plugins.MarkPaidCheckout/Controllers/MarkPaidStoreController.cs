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
[Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
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

    [HttpGet]
    public IActionResult StoreConfig()
    {
        var store = StoreData;
        var blob = store.GetStoreBlob();
        var vm = new StoreConfigVm();
        foreach (var m in registry.Methods)
        {
            var pmid = new PaymentMethodId(m);
            var cfg = store.GetPaymentMethodConfig<MarkPaidPaymentMethodConfig>(pmid, handlers) ?? new MarkPaidPaymentMethodConfig();
            store.SetPaymentMethodConfig(handlers[pmid], cfg);
            var enabled = !blob.GetExcludedPaymentMethods().Match(pmid);
            vm.Methods.Add(new StoreMethodItem { Method = m, Enabled = enabled });
        }

        return View("StoreConfig", vm);
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
        }

        store.SetStoreBlob(blob);
        await storeRepository.UpdateStore(store);
        return RedirectToAction(nameof(StoreConfig), new { storeId = store.Id });
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
