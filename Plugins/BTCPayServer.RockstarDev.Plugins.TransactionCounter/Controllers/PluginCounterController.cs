using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.RockstarDev.Plugins.TransactionCounter.ViewModels;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.RockstarDev.Plugins.TransactionCounter.Controllers;

[Route("server/stores/counter")]
[Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class TransactionCounterController(
    StoreRepository storeRepository,
    SettingsRepository settingsRepository,
    UserManager<ApplicationUser> userManager,
    InvoiceRepository invoiceRepository) : Controller
{
    private StoreData StoreData => HttpContext.GetStoreData();

    [HttpGet]
    public async Task<IActionResult> CounterConfig()
    {

        var stores = await storeRepository.GetStores();
        var model = await settingsRepository.GetSettingAsync<CounterPluginSettings>() ?? new CounterPluginSettings { AllStores = true };
        var vm = new CounterConfigViewModel
        {
            StartDate = model.StartDate,
            EndDate = model.EndDate,
            AllStores = model.AllStores,
            Password = model.Password,
            Enabled = model.Enabled,
            SelectedStores = model.SelectedStores,
            CustomHtmlTemplate = model.CustomHtmlTemplate,
            BackgroundVideoUrl = model.BackgroundVideoUrl,
            Stores = stores
        };
        return View(vm);
    }

    [HttpPost]
    public async Task<IActionResult> CounterConfig(CounterConfigViewModel viewModel)
    {
        if (!string.IsNullOrEmpty(viewModel.BackgroundVideoUrl) && !IsValidUrl(viewModel.BackgroundVideoUrl))
        {
            ModelState.AddModelError(nameof(viewModel.BackgroundVideoUrl), "Please enter a valid URL.");
        }
        if (viewModel.AllStores)
        {
            foreach (var item in viewModel.SelectedStores)
                item.Enabled = true;
        }
        var settings = new CounterPluginSettings
        {
            CustomHtmlTemplate = viewModel.CustomHtmlTemplate,
            BackgroundVideoUrl = viewModel.BackgroundVideoUrl,
            StartDate = viewModel.StartDate,
            EndDate = viewModel.EndDate,
            Enabled = viewModel.Enabled,
            Password = viewModel.Password,
            AllStores = viewModel.AllStores,
            AdminUserId = GetUserId(),
            SelectedStores = viewModel.SelectedStores.Where(c => c.Enabled).ToArray()
        };
        await settingsRepository.UpdateSetting(settings);
        TempData[WellKnownTempData.SuccessMessage] = "Plugin counter configuration updated successfully";
        return RedirectToAction(nameof(CounterConfig));
    }


    [HttpGet("tx-counter")]
    [AllowAnonymous]
    public async Task<IActionResult> Counter()
    {
        var model = await settingsRepository.GetSettingAsync<CounterPluginSettings>() ?? new CounterPluginSettings { AllStores = true };
        if (!model.Enabled)
            return NotFound();

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
        var vm = new CounterViewModel {  TransactionCount = invoiceCount };
        return View(vm);
    }

    private string GetUserId() => userManager.GetUserId(User);
    private static bool IsValidUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uriResult)
            && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
    }

}
