using System;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.RockstarDev.Plugins.CreditCheckout.PaymentHandlers;
using BTCPayServer.RockstarDev.Plugins.CreditCheckout.ViewModels;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InvoiceData = BTCPayServer.Client.Models.InvoiceData;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.RockstarDev.Plugins.CreditCheckout.Controllers;

[Route("stores/{storeId}/credit")]
[Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class CreditController(
    StoreRepository storeRepository,
    InvoiceRepository invoiceRepository,
    PaymentMethodHandlerDictionary handlers,
    PaymentService paymentService,
    CreditStatusProvider creditStatusProvider) : Controller
{
    private StoreData StoreData => HttpContext.GetStoreData();

    [HttpGet]
    public async Task<IActionResult> StoreConfig()
    {
        var model = new CreditStoreViewModel { Enabled = await creditStatusProvider.CreditEnabled(StoreData.Id) };

        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> StoreConfig(CreditStoreViewModel viewModel)
    {
        var store = StoreData;
        var blob = StoreData.GetStoreBlob();
        var paymentMethodId = CreditCheckoutPlugin.CreditPmid;
        var currentPaymentMethodConfig = StoreData.GetPaymentMethodConfig<CreditPaymentMethodConfig>(paymentMethodId, handlers);
        currentPaymentMethodConfig ??= new CreditPaymentMethodConfig();

        blob.SetExcluded(paymentMethodId, !viewModel.Enabled);

        StoreData.SetPaymentMethodConfig(handlers[paymentMethodId], currentPaymentMethodConfig);
        store.SetStoreBlob(blob);
        await storeRepository.UpdateStore(store);

        return RedirectToAction("StoreConfig", new { storeId = store.Id, paymentMethodId });
    }


    [HttpGet("MarkAsPaid")]
    [AllowAnonymous]
    public async Task<IActionResult> MarkAsPaid(string invoiceId, string storeId, string returnUrl)
    {
        var invoice = await invoiceRepository.GetInvoice(invoiceId, true);

        if (invoice.StoreId != storeId ||
            invoice.Status != InvoiceStatus.New)
            return Redirect(returnUrl);

        // Add Payment in Credit to Invoice
        var handler = handlers[CreditCheckoutPlugin.CreditPmid];
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
            if (!await invoiceRepository.MarkInvoiceStatus(invoice.Id, InvoiceStatus.Settled))
            {
                //ModelState.AddModelError(nameof(request.Status),
                //    "Status can only be marked to invalid or settled within certain conditions.");
            }

        return Redirect(returnUrl);
    }
}
