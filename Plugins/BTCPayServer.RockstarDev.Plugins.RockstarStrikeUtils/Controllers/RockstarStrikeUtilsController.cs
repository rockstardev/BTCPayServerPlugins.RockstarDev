using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Data;
using BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Data.Models;
using BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Logic;
using BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.ViewModels.RockstarStrikeUtils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Strike.Client.CurrencyExchanges;
using Strike.Client.Models;
using Strike.Client.ReceiveRequests.Requests;

namespace BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Controllers;

[Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Route("~/plugins/{storeId}/rockstarstrikeutils")]
public class RockstarStrikeUtilsController(
    RockstarStrikeDbContextFactory strikeDbContextFactory,
    StrikeClientFactory strikeClientFactory) : Controller
{
    [FromRoute]
    public string StoreId { get; set; }
    
    [HttpGet("index")]
    public async Task<IActionResult> Index()
    {
        var isSetup = await strikeClientFactory.ClientExistsAsync();
        return RedirectToAction(isSetup ? nameof(ReceiveRequests) : nameof(Configuration), new { StoreId});
    }

    [HttpGet("Configuration")]
    public async Task<IActionResult> Configuration()
    {
        await using var db = strikeDbContextFactory.CreateContext();

        var model = new ConfigurationViewModel
        {
            StrikeApiKey = db.Settings.SingleOrDefault(a => a.StoreId == StoreId && a.Key == DbSetting.StrikeApiKey)?.Value
        };
        
        return View(model);
    }

    [HttpPost("Configuration")]
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
    
    // Payment Methods
    [HttpGet("PaymentMethods")]
    public async Task<IActionResult> PaymentMethods()
    {
        var client = await strikeClientFactory.ClientCreateAsync();
        if (client == null)
            return RedirectToAction(nameof(Configuration));
        
        var resp = await client.PaymentMethods.GetPaymentMethods();
        var model = new PaymentMethodsViewModel
        {
            List = resp.Items.ToList(),
            TotalCount = resp.Count
        };
        
        return View(model);
    }
    
    //  Receive Requests
    [HttpGet("ReceiveRequests")]
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
    
    [HttpGet("ReceiveRequests/create")]
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
    
    [HttpPost("ReceiveRequests/create")]
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
    [HttpGet("CurrencyExchanges")]
    public async Task<IActionResult> CurrencyExchanges(string operation, decimal amount)
    {
        var client = await strikeClientFactory.ClientCreateAsync();
        if (client == null)
            return RedirectToAction(nameof(Configuration));
        
        var resp = await client.Balances.GetBalances();
        var model = new CurrencyExchangesViewModel()
        {
            Balances = resp.ToList(),
            Operation = operation,
            Amount = amount
        };

        if (!string.IsNullOrEmpty(operation))
        {
            CurrencyExchangeQuoteReq req = null;
            if (operation == "BuyBitcoin")
            {
                if (resp.Single(a=>a.Currency == Currency.Usd).Available < amount)
                {
                    ModelState.AddModelError(nameof(model.Amount), "Insufficient USD funds.");
                    return View(nameof(CurrencyExchanges), model);
                }
                req = new CurrencyExchangeQuoteReq
                {
                    Buy = Currency.Btc, Sell = Currency.Usd,
                    Amount = new MoneyWithFee
                    {
                        Currency = Currency.Usd, Amount = amount, FeePolicy = FeePolicy.Inclusive
                    }
                };
            }
            else if (operation == "SellBitcoin")
            {
                if (resp.Single(a=>a.Currency == Currency.Btc).Available < amount)
                {
                    ModelState.AddModelError(nameof(model.Amount), "Insufficient BTC funds.");
                    return View(nameof(CurrencyExchanges), model);
                }
                req = new CurrencyExchangeQuoteReq
                {
                    Buy = Currency.Usd, Sell = Currency.Btc,
                    Amount = new MoneyWithFee
                    {
                        Currency = Currency.Btc, Amount = amount, FeePolicy = FeePolicy.Exclusive
                    }
                };
            }
            else
                throw new InvalidOperationException("Invalid operation");

            var exchangeResp = await client.CurrencyExchanges.CreateQuote(req);
            if (!exchangeResp.IsSuccessStatusCode)
            {
                ModelState.AddModelError(nameof(model.Amount), "Quote not generated: " + exchangeResp.Error);
            }
            model.Quote = exchangeResp;
        }
        
        return View(model);
    }
    
    [HttpPost("CurrencyExchangesProcess")]
    public async Task<IActionResult> CurrencyExchangesProcess(Guid quoteId)
    {
        var client = await strikeClientFactory.ClientCreateAsync();
        if (client == null)
            return RedirectToAction(nameof(Configuration));
        
        var resp = await client.CurrencyExchanges.GetQuote(quoteId);
        if (!resp.IsSuccessStatusCode || resp.State != CurrencyExchangeState.New)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Message = $"Failed to get pending quote: {resp.Error}",
                Severity = StatusMessageModel.StatusSeverity.Error
            });
            return RedirectToAction(nameof(CurrencyExchanges));
        }

        var executeQuote = await client.CurrencyExchanges.ExecuteQuote(quoteId);
        if (executeQuote.IsSuccessStatusCode)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Message = $"Quote executed successfully.",
                Severity = StatusMessageModel.StatusSeverity.Success
            });
        }
        else
        {
            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Message = $"Failed to execute quote: {executeQuote.Error}",
                Severity = StatusMessageModel.StatusSeverity.Error
            });
        }

        return RedirectToAction(nameof(CurrencyExchanges));
    }
}