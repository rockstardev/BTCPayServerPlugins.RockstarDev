using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.RockstarDev.Plugins.CashCheckout.PaymentHandlers;
using BTCPayServer.RockstarDev.Plugins.CashCheckout.ViewModels;
using BTCPayServer.RockstarDev.Plugins.CashCheckoutMethod;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.RockstarDev.Plugins.CashCheckout.Controllers;

[Route("stores/{storeId}/cash")]
[Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class CashController(
    StoreRepository storeRepository,
    InvoiceRepository invoiceRepository,
    PaymentMethodHandlerDictionary handlers,
    CashCheckoutConfigurationItem cashMethod,
    CashStatusProvider cashStatusProvider) : Controller
{
    private StoreData StoreData => HttpContext.GetStoreData();
    
    [HttpGet]
    public async Task<IActionResult> StoreConfig()
    {
        var model = new CashStoreViewModel
        {
            Enabled = await cashStatusProvider.CashEnabled(StoreData.Id)
        };
        
        return View(model);
    }
    
    [HttpPost]
    public async Task<IActionResult> StoreConfig(CashStoreViewModel viewModel,
        PaymentMethodId paymentMethodId)
    {
        var store = StoreData;
        var blob = StoreData.GetStoreBlob();
        var currentPaymentMethodConfig = StoreData.GetPaymentMethodConfig<CashPaymentMethodConfig>(paymentMethodId, handlers);
        currentPaymentMethodConfig ??= new CashPaymentMethodConfig();
        
        // else if (viewModel.Enabled == blob.IsExcluded(paymentMethodId))
        // {
        //     blob.SetExcluded(paymentMethodId, !viewModel.Enabled);
        //
        //     TempData.SetStatusMessageModel(new StatusMessageModel
        //     {
        //         Message = $"{paymentMethodId} is now {(viewModel.Enabled ? "enabled" : "disabled")}",
        //         Severity = StatusMessageModel.StatusSeverity.Success
        //     });
        // }
        blob.SetExcluded(paymentMethodId, !viewModel.Enabled);

        StoreData.SetPaymentMethodConfig(handlers[paymentMethodId], currentPaymentMethodConfig);
        store.SetStoreBlob(blob);
        await storeRepository.UpdateStore(store);


        return RedirectToAction("StoreConfig", new { storeId = store.Id, paymentMethodId });
    }


    [HttpGet("MarkAsPaid")]
    [AllowAnonymous]
    public async Task<IActionResult> MarkAsPaid(string invoiceId, string returnUrl)
    {
        var invoice = await invoiceRepository.GetInvoice(invoiceId, true);

        if (invoice.StoreId != StoreData.Id ||
            invoice.Status != InvoiceStatus.New)
        {
            return Redirect(returnUrl);
            //return InvoiceNotFound();
        }

        // TODO: Add Payment in Cash to invoice
        
        if (!await invoiceRepository.MarkInvoiceStatus(invoice.Id, InvoiceStatus.Settled))
        {
            //ModelState.AddModelError(nameof(request.Status),
            //    "Status can only be marked to invalid or settled within certain conditions.");
        }
        
        return Redirect(returnUrl);
    }
}