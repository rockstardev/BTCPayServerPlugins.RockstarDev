using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Data;
using BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Data.Models;
using BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Logic;
using BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.ViewModels.RockstarStrikeUtils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Strike.Client;
using Strike.Client.Models;
using Strike.Client.ReceiveRequests.Requests;

namespace BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Controllers;

[Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class RockstarStrikeUtilsController(
    RockstarStrikeDbContextFactory strikeDbContextFactory,
    StrikeClientFactory strikeClientFactory) : Controller
{
    [HttpGet("~/plugins/rockstarstrikeutils/index")]
    public async Task<IActionResult> Index()
    {
        var isSetup = await strikeClientFactory.ClientExistsAsync();
        return RedirectToAction(isSetup ? nameof(ReceiveRequests) : nameof(Configuration));
    }

    [HttpGet("~/plugins/rockstarstrikeutils/Configuration")]
    public async Task<IActionResult> Configuration()
    {
        await using var db = strikeDbContextFactory.CreateContext();

        var model = new ConfigurationViewModel
        {
            StrikeApiKey = db.Settings.SingleOrDefault(a => a.Key == "StrikeApiKey")?.Value
        };
        
        return View(model);
    }

    [HttpPost("~/plugins/rockstarstrikeutils/Configuration")]
    public async Task<IActionResult> Configuration(ConfigurationViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var validKey = await strikeClientFactory.TestAndSaveApiKeyAsync(model.StrikeApiKey);
        if (!validKey)
        {
            ModelState.AddModelError(nameof(model.StrikeApiKey), "Invalid API key.");
        }
        else
        {
            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Message = $"Strike API key saved successfully.",
                Severity = StatusMessageModel.StatusSeverity.Success
            });
        }

        return View(model);
    }
    
    //  Receive Requests
    [HttpGet("~/plugins/rockstarstrikeutils/ReceiveRequests")]
    public async Task<IActionResult> ReceiveRequests()
    {
        var client = await strikeClientFactory.ClientCreateAsync();
        if (client == null)
            return RedirectToAction(nameof(Configuration));
        
        var requests = await client.ReceiveRequests.GetRequests();
        var model = new ReceiveRequestsViewModel
        {
            ReceiveRequests = requests.Items.ToList(),
            TotalCount = requests.Count
        };
        
        return View(model);
    }
    
    [HttpGet("~/plugins/rockstarstrikeutils/ReceiveRequests/create")]
    public async Task<IActionResult> ReceiveRequestsCreate()
    {
        var client = await strikeClientFactory.ClientCreateAsync();
        if (client == null)
            return RedirectToAction(nameof(Configuration));

        var model = new ReceiveRequestsCreateViewModel
        {
            TargetCurrency = "USD",
            Onchain = true
        };
        return View(model);
    }
    
    [HttpPost("~/plugins/rockstarstrikeutils/ReceiveRequests/create")]
    public async Task<IActionResult> ReceiveRequestsCreate(ReceiveRequestsCreateViewModel model)
    {
        var client = await strikeClientFactory.ClientCreateAsync();
        if (client == null)
            return RedirectToAction(nameof(Configuration));
        
        if (!ModelState.IsValid)
            return View(model);

        var req = new ReceiveRequestReq
        {
            TargetCurrency = model.TargetCurrency switch
            {
                "USD" => Currency.Usd,
                "EUR" => Currency.Eur,
                "BTC" => Currency.Btc,
                _ => throw new ArgumentException("Invalid currency")
            },
            Onchain = model.Onchain
                ? new OnchainReceiveRequestReq()
                : null
        };
        var resp = await client.ReceiveRequests.Create(req);
        if (resp.IsSuccessStatusCode)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Message = $"Created Receive Request {resp.ReceiveRequestId}",
                Severity = StatusMessageModel.StatusSeverity.Success
            });
        }
        else
        {
            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Message = $" Receive Request creation failed: {resp.Error}",
                Severity = StatusMessageModel.StatusSeverity.Error
            });
        }

        return RedirectToAction(nameof(ReceiveRequests));
    }
    
    //  Exchanges
    [HttpGet("~/plugins/rockstarstrikeutils/CurrencyExchanges")]
    public async Task<IActionResult> CurrencyExchanges()
    {
        var client = await strikeClientFactory.ClientCreateAsync();
        if (client == null)
            return RedirectToAction(nameof(Configuration));
        
        var resp = await client.Balances.GetBalances();
        var model = new CurrencyExchangesViewModel()
        {
            Balances = resp.ToList()
        };
        
        return View(model);
    }
}