using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.RockstarDev.Plugins.TransactionCounter.ViewModels;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace BTCPayServer.RockstarDev.Plugins.TransactionCounter.Controllers;

[AllowAnonymous]
[Route("txcounter/")]
public class PublicCounterController(
    UriResolver uriResolver,
    StoreRepository storeRepo,
    StoreRepository storeRepository,
    SettingsRepository settingsRepository,
    InvoiceRepository invoiceRepository) : Controller
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
        string htmlContent = model.HtmlTemplate.Replace("{COUNTER}", transactionCount.ToString());
        return Content(htmlContent, "text/html");
    }


    [HttpGet("api")]
    public async Task<IActionResult> ApiCounter([FromQuery] string password)
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
        var transactionCount = await TransactionCountQuery(model);
        return Json(new { count = transactionCount });
    }

    private async Task<int> TransactionCountQuery(CounterPluginSettings model)
    {
        var stores = await storeRepository.GetStores();
        var allStoreIds = stores.Where(c => !c.Archived).Select(s => s.Id).ToArray();
        var excludedStoreIds = (model.ExcludedStoreIds ?? "").Split(",", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var includedStoreIds = allStoreIds.Where(id => !excludedStoreIds.Contains(id)).ToArray();
        var query = new InvoiceQuery
        {
            StartDate = model.StartDate,
            EndDate = model.EndDate,
            Status = new[] { InvoiceStatus.Processing.ToString(), InvoiceStatus.Settled.ToString() },
            StoreId = includedStoreIds.Length > 0 ? includedStoreIds : allStoreIds
        };
        var transactionCount = await invoiceRepository.GetInvoiceCount(query);
        return transactionCount + CalculateExtraTransactionCount(model);
    }

    int CalculateExtraTransactionCount(CounterPluginSettings model)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(model.ExtraTransactions)) 
                return 0;

            var now = DateTime.UtcNow;
            var extraTransaction = JsonConvert.DeserializeObject<List<ExtraTransactionEntry>>(model.ExtraTransactions) ?? new();
            int total = 0;
            foreach (var txn in extraTransaction)
            {
                if (now < txn.Start)
                    continue;
                if (now >= txn.End)
                {
                    total += txn.Count;
                }
                else
                {
                    var duration = (txn.End - txn.Start).TotalSeconds;
                    var elapsed = (now - txn.Start).TotalSeconds;
                    var ratio = elapsed / duration;
                    total += (int)(txn.Count * ratio);
                }
            }
            return total;
        }
        catch { return 0; }
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
