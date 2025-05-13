using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.RockstarDev.Plugins.PluginCounter.ViewModels;
using BTCPayServer.RockstarDev.Plugins.PluginCounter;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BTCPayServer.Client.Models;
using BTCPayServer.Models;
using BTCPayServer.Services.Stores;
using BTCPayServer.Data;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Controllers;

[AllowAnonymous]
[Route("server/stores/")]
public class PublicCounterController(
    UriResolver uriResolver,
    StoreRepository storeRepo,
    SettingsRepository settingsRepository,
    InvoiceRepository invoiceRepository) : Controller
{
    [HttpGet("tx-counter")]
    public async Task<IActionResult> Counter([FromQuery] string password)
    {
        var model = await settingsRepository.GetSettingAsync<CounterPluginSettings>() ?? new CounterPluginSettings { AllStores = true };
        if (!model.Enabled)
            return NotFound();

        if (!string.IsNullOrEmpty(model.Password))
        {
            var validationResult = await ValidatePassword(model, password);
            if (validationResult != null)
                return validationResult;
        }
        var query = new InvoiceQuery
        {
            StartDate = model.StartDate,
            EndDate = model.EndDate,
            Status = new[] { InvoiceStatus.Processing.ToString(), InvoiceStatus.Settled.ToString() },
            StoreId = model.AllStores ? null : model.SelectedStores?
                    .Where(s => s.Enabled)
                    .Select(s => s.Id)
                    .ToArray()
        };
        var invoiceCount = await invoiceRepository.GetInvoiceCount(query);
        var vm = new CounterViewModel { TransactionCount = invoiceCount };
        return View(vm);
    }


    [HttpGet("tx-counter/api/count")]
    public async Task<IActionResult> ApiCounter([FromQuery] string password)
    {
        var model = await settingsRepository.GetSettingAsync<CounterPluginSettings>() ?? new CounterPluginSettings { AllStores = true };
        if (!model.Enabled)
            return NotFound();

        if (!string.IsNullOrEmpty(model.Password))
        {
            var validationResult = await ValidatePassword(model, password);
            if (validationResult != null)
                return validationResult;
        }
        var query = new InvoiceQuery
        {
            StartDate = model.StartDate,
            EndDate = model.EndDate,
            Status = new[] { InvoiceStatus.Processing.ToString(), InvoiceStatus.Settled.ToString() },
            StoreId = model.AllStores ? null : model.SelectedStores?
                    .Where(s => s.Enabled)
                    .Select(s => s.Id)
                    .ToArray()
        };
        var invoiceCount = await invoiceRepository.GetInvoiceCount(query);
        return Json(new { count = invoiceCount });
    }


    private async Task<IActionResult> ValidatePassword(CounterPluginSettings model, string password)
    {
        if (string.IsNullOrEmpty(password) || password != model.Password)
        {
            var adminStores = await storeRepo.GetStoresByUserId(model.AdminUserId);
            var storeData = adminStores[0];
            var publicModel = new BaseCounterPublicViewModel
            {
                StoreId = storeData.Id,
                StoreName = storeData?.StoreName,
                StoreBranding = await StoreBrandingViewModel.CreateAsync(Request, uriResolver, storeData.GetStoreBlob()),
            };
            return View("PasswordRequired", publicModel);
        }
        return null;
    }
}
