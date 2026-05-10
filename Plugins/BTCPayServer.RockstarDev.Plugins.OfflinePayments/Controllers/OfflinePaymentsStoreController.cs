using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.RockstarDev.Plugins.OfflinePayments.PaymentHandlers;
using BTCPayServer.RockstarDev.Plugins.OfflinePayments.Services;
using BTCPayServer.RockstarDev.Plugins.OfflinePayments.ViewModels;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.RockstarDev.Plugins.OfflinePayments.Controllers;

[Route("~/plugins/{storeId}/offline-payments/")]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanModifyStoreSettings)]
public class OfflinePaymentsStoreController(OfflineMethodConfigService configService,
    OfflinePaymentsService paymentsService,
    StoreRepository storeRepository,
    PaymentMethodHandlerDictionary handlers) : Controller
{
    private StoreData StoreData => HttpContext.GetStoreData();

    [HttpGet]
    public async Task<IActionResult> Index(string storeId)
    {
        var methods = await configService.GetAllMethods(storeId);
        return View(new OfflineSettingsViewModel
        {
            StoreId = storeId,
            Methods = methods
        });
    }

    [HttpGet("create")]
    public IActionResult Create(string storeId)
    {
        return View("EditMethod", new OfflineMethodConfigViewModel
        {
            StoreId = storeId,
            AvailableMethodTypes = configService.GetMethodTypes()
        });
    }

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string storeId, OfflineMethodConfigViewModel vm)
    {
        vm.AvailableMethodTypes = configService.GetMethodTypes();
        if (!ModelState.IsValid)
            return View("EditMethod", vm);

        if (await configService.PaymentMethodExists(storeId, vm.MethodId))
        {
            ModelState.AddModelError(nameof(vm.MethodId), $"A payment method with ID '{vm.MethodId}' already exists for this store.");
            vm.AvailableMethodTypes = configService.GetMethodTypes();
            return View("EditMethod", vm);
        }
        await configService.Create(vm.ToModel(storeId));
        var pmid = new PaymentMethodId(vm.MethodId);
        await EnableMethodInStore(StoreData, pmid);
        TempData["SuccessMessage"] = $"Payment method '{vm.DisplayName}' created.";
        return RedirectToAction(nameof(Index), new { storeId });
    }

    [HttpGet("{id}/edit")]
    public async Task<IActionResult> Edit(string storeId, string id)
    {
        var method = await configService.GetMethodOptionById(id, storeId);
        if (method is null)
            return NotFound();

        return View("EditMethod", OfflineMethodConfigViewModel.FromModel(method));
    }

    [HttpPost("{id}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string storeId, string id, OfflineMethodConfigViewModel vm)
    {
        if (!ModelState.IsValid)
            return View("EditMethod", vm);

        vm.Id = id;
        var updated = await configService.Update(vm.ToModel(storeId));
        if (updated is null)
            return NotFound();

        var pmid = new PaymentMethodId(vm.MethodId);
        if (vm.IsEnabled)
            await EnableMethodInStore(StoreData, pmid);
        else
            await DisableMethodInStore(StoreData, pmid);

        TempData["SuccessMessage"] = $"Payment method '{vm.DisplayName}' updated.";
        return RedirectToAction(nameof(Index), new { storeId });
    }

    [HttpPost("{id}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string storeId, string id)
    {
        await configService.Delete(id, storeId);
        TempData["SuccessMessage"] = "Payment method deleted.";
        return RedirectToAction(nameof(Index), new { storeId });
    }

    [HttpGet("pending")]
    public async Task<IActionResult> PendingQueue(string storeId, string method = null)
    {
        var items = await paymentsService.GetPendingPaymentQueue(storeId, method);
        var allMethods = await configService.GetAllMethods(storeId);

        return View(new OfflinePendingQueueViewModel
        {
            StoreId = storeId,
            Items = items,
            AvailableMethodIds = allMethods.Select(m => m.MethodId).Distinct().ToList(),
            MethodIdFilter = method
        });
    }

    [HttpPost("pending/{pendingId}/confirm")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(string storeId, string pendingId)
    {
        var userId = User.Claims.FirstOrDefault(c => c.Type == "sub")?.Value ?? "unknown";
        var result = await paymentsService.AdminConfirmPayment(pendingId, storeId, userId);

        TempData[result is null ? "ErrorMessage" : "SuccessMessage"] =
            result is null ? "Payment record not found." : "Invoice settled successfully.";

        return RedirectToAction(nameof(PendingQueue), new { storeId });
    }

    [HttpPost("pending/{pendingId}/invalidate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Invalidate(string storeId, string pendingId)
    {
        var userId = User.Claims.FirstOrDefault(c => c.Type == "sub")?.Value ?? "unknown";
        var result = await paymentsService.AdminInvalidatePayment(pendingId, storeId, userId);

        TempData[result is null ? "ErrorMessage" : "SuccessMessage"] =
            result is null ? "Payment record not found." : "Payment invalidated.";

        return RedirectToAction(nameof(PendingQueue), new { storeId });
    }

    private void EnsureConfigEntry(StoreData store, PaymentMethodId pmid)
    {
        var existing = store.GetPaymentMethodConfig(pmid, handlers);
        if (existing is null)
            store.SetPaymentMethodConfig(handlers[pmid], new OfflinePaymentMethodConfig());
    }

    private async Task EnableMethodInStore(StoreData store, PaymentMethodId pmid)
    {
        var blob = store.GetStoreBlob();
        blob.SetExcluded(pmid, false);
        EnsureConfigEntry(store, pmid);
        store.SetStoreBlob(blob);
        await storeRepository.UpdateStore(store);
    }

    private async Task DisableMethodInStore(StoreData store, PaymentMethodId pmid)
    {
        var blob = store.GetStoreBlob();
        blob.SetExcluded(pmid, true);
        store.SetStoreBlob(blob);
        await storeRepository.UpdateStore(store);
    }
}
