using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.RockstarDev.Plugins.TransactionCounter.Services;
using BTCPayServer.RockstarDev.Plugins.TransactionCounter.ViewModels;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace BTCPayServer.RockstarDev.Plugins.TransactionCounter.Controllers;

[AllowAnonymous]
[Route("txcounter/")]
public class PublicCounterController(
    UriResolver uriResolver,
    StoreRepository storeRepo,
    SettingsRepository settingsRepository,
    TransactionCounter transactionCounter) : Controller
{

    [HttpGet("html")]
    public async Task<IActionResult> Counter([FromQuery] string password)
    {
        var model = await settingsRepository.GetSettingAsync<CounterPluginSettings>() ?? new();
        if (!model.Enabled)
            return NotFound();

        if (!string.IsNullOrEmpty(model.Password))
        {
            var validationResult = await ValidatePassword(model, password);
            if (validationResult != null)
                return validationResult;
        }

        if (string.IsNullOrEmpty(model.HtmlTemplate) ||
            !model.HtmlTemplate.Contains("{COUNTER}") ||
            !model.HtmlTemplate.Contains("<html", StringComparison.OrdinalIgnoreCase) ||
            !model.HtmlTemplate.Contains("<body", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Invalid HTML template or missing {COUNTER} placeholder");
        }

        var transactionCount = await TransactionCountQuery(model);
        var viewModel = new CounterViewModel { HtmlTemplate = model.HtmlTemplate, InitialCount = transactionCount };
        return View(viewModel);
    }


    [HttpGet("api")]
    public async Task<IActionResult> ApiCounter([FromQuery] string password)
    {
        // Add CORS headers directly to the response
        Response.Headers.Append("Access-Control-Allow-Origin", "*");
        Response.Headers.Append("Access-Control-Allow-Headers", "*");
        Response.Headers.Append("Access-Control-Allow-Methods", "GET");

        var model = await settingsRepository.GetSettingAsync<CounterPluginSettings>() ?? new();
        if (!model.Enabled)
            return NotFound();

        if (!string.IsNullOrEmpty(model.Password))
        {
            var validationResult = await ValidatePassword(model, password);
            if (validationResult != null)
                return validationResult;
        }

        var transactionCount = await TransactionCountQuery(model);
        return Json(new { count = transactionCount });
    }

    private Task<int> TransactionCountQuery(CounterPluginSettings model)
    {
        return transactionCounter.GetTransactionCountAsync(model);
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
                StoreBranding = await StoreBrandingViewModel.CreateAsync(Request, uriResolver, storeData.GetStoreBlob())
            };
            return View("PasswordRequired", publicModel);
        }

        return null;
    }
}
