using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.RockstarDev.Plugins.TransactionCounter.ViewModels;
using BTCPayServer.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.RockstarDev.Plugins.TransactionCounter.Controllers;

[Route("server/stores/counter")]
[Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class TransactionCounterController(
    StoreRepository storeRepository,
    SettingsRepository settingsRepository,
    UserManager<ApplicationUser> userManager) : Controller
{

    [HttpGet]
    [Route("DefaultHtmlTemplate")]
    public IActionResult DefaultHtmlTemplate()
    {
        return Content(HtmlTemplates.Default);
    }

    [HttpGet]
    public async Task<IActionResult> CounterConfig()
    {
        var stores = await storeRepository.GetStores();
        stores = stores.Where(c => !c.Archived).ToArray();
        var model = await settingsRepository.GetSettingAsync<CounterPluginSettings>() ?? new();
        var vm = new CounterConfigViewModel
        {
            StartDate = model.StartDate,
            EndDate = model.EndDate,
            Password = model.Password,
            Enabled = model.Enabled,
            IncludeArchived = model.IncludeArchived,
            IncludeTransactionVolume = model.IncludeTransactionVolume,
            HtmlTemplate = string.IsNullOrWhiteSpace(model.HtmlTemplate) ? HtmlTemplates.Default : model.HtmlTemplate,
            ExtraTransactions = model.ExtraTransactions,
            Stores = stores,
            ExcludedStoreIds = model.ExcludedStoreIds
        };
        return View(vm);
    }

    [HttpPost]
    public async Task<IActionResult> CounterConfig(CounterConfigViewModel viewModel)
    {
        if (string.IsNullOrEmpty(viewModel.HtmlTemplate))
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = "HTML Template cannot be empty.",
                Severity = StatusMessageModel.StatusSeverity.Error
            });
            return RedirectToAction(nameof(CounterConfig));
        }

        var settings = new CounterPluginSettings
        {
            HtmlTemplate = viewModel.HtmlTemplate,
            StartDate = viewModel.StartDate,
            EndDate = viewModel.EndDate,
            Enabled = viewModel.Enabled,
            IncludeArchived = viewModel.IncludeArchived,
            IncludeTransactionVolume = viewModel.IncludeTransactionVolume,
            ExtraTransactions = viewModel.ExtraTransactions,
            Password = viewModel.Password,
            AdminUserId = GetUserId(),
            ExcludedStoreIds = viewModel.ExcludedStoreIds
        };
        await settingsRepository.UpdateSetting(settings);
        TempData[WellKnownTempData.SuccessMessage] = "Plugin counter configuration updated successfully";
        return RedirectToAction(nameof(CounterConfig));
    }

    private string GetUserId() => userManager.GetUserId(User);
}
