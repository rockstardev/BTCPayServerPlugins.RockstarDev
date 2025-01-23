using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Data;
using BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Data.Models;
using BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Logic;
using BTCPayServer.RockstarDev.Plugins.BitcoinStacker.ViewModels.ExchangeOrder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Controllers;

[Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Route("~/plugins/{storeId}/exchangeorder")]
public class ExchangeOrderController(
    PluginDbContextFactory pluginDbContextFactory,
    StrikeClientFactory strikeClientFactory) : Controller
{
    [FromRoute]
    public string StoreId { get; set; }

    [HttpGet("index")]
    public async Task<IActionResult> Index()
    {
        await using var db = pluginDbContextFactory.CreateContext();
        var list = db.ExchangeOrders
            .Where(a => a.StoreId == StoreId)
            .OrderByDescending(a => a.CreatedForDate)
            .ThenByDescending(a => a.Created)
            .ToList();
        var viewModel = new IndexViewModel { List = list };
        
        // TODO: Have the BTC balance on Strike in the database ready to fetch
        // viewModel.BitcoinBalance = "6.15";
        
        return View(viewModel);
    }

    [HttpGet("IndexLogs/{id}")]
    public async Task<IActionResult> IndexLogs(string id)
    {
        if (!Guid.TryParse(id, out var guid))
            return RedirectToAction(nameof(Index));
        
        await using var db = pluginDbContextFactory.CreateContext();
        var item = db.ExchangeOrders
            .Include(a => a.ExchangeOrderLogs)
            .SingleOrDefault(a => a.StoreId == StoreId && a.Id == guid);
        
        if (item == null)
            return NotFound();
        
        var viewModel = new IndexLogsViewModel { Item = item };
        return View(viewModel);
    }
    
    

    [HttpGet("ClearDelayUntil")]
    public ActionResult ClearDelayUntil(string id)
    {
        return View("Confirm", new ConfirmModel
        {
            Title = "Clear Delay?",
            Description = "Are you sure you want to clear the delay on this Exchange Order?",
            Action = "Yes"
        });
    }

    [HttpPost("ClearDelayUntil")]
    public async Task<IActionResult> ClearDelayUntilPost(Guid id)
    {
        var db = pluginDbContextFactory.CreateContext();

        var order = db.ExchangeOrders.Single(a => a.Id == id);
        order.DelayUntil = null;
        await db.SaveChangesAsync();

        TempData[WellKnownTempData.SuccessMessage] =
            $"Delay on Exchange Order {id} has been cleared.";

        return RedirectToAction(nameof(Index), new { StoreId });
    }
    

    [HttpGet("create")]
    public IActionResult Create()
    {
        var viewModel = new CreateExchangeOrderViewModel();
        return View(viewModel);
    }

    [HttpPost("create")]
    public async Task<IActionResult> Create([FromForm] CreateExchangeOrderViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        await using var db = pluginDbContextFactory.CreateContext();
        var exchangeOrder = new DbExchangeOrder
        {
            StoreId = StoreId,
            Operation = model.Operation,
            Amount = model.Amount,
            DelayUntil = model.DelayUntil?.UtcDateTime,
            Created = DateTimeOffset.UtcNow,
            State = DbExchangeOrder.States.Created,
            CreatedBy = "Manual"
        };
        db.ExchangeOrders.Add(exchangeOrder);
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index), new { StoreId });
    }

    [HttpGet("settings")]
    public async Task<IActionResult> Settings()
    {
        await using var db = pluginDbContextFactory.CreateContext();
        var dbSetting = await db.Settings.FirstOrDefaultAsync(a => a.StoreId == StoreId && a.Key == DbSettingKeys.ExchangeOrderSettings.ToString());

        var viewModel = SettingsViewModel.FromDbSettings(dbSetting);
        return View(viewModel);
    }

    [HttpPost("settings")]
    public async Task<IActionResult> Settings([FromForm] SettingsViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        // automatically set up ACH deposit method
        if (!String.IsNullOrEmpty(model.StrikeApiKey) && model.StrikePaymentMethodId == Guid.Empty)
        {
            var paymentMethodAch = await strikeClientFactory.IsApiKeyValidAch(model.StrikeApiKey);
            if (paymentMethodAch == null)
            {
                ModelState.AddModelError(nameof(model.StrikeApiKey), "Invalid API key.");
                return View(model);
            }

            model.StrikePaymentMethodId = Guid.Parse(paymentMethodAch);
        }

        await using var db = pluginDbContextFactory.CreateContext();
        var dbSetting = await db.Settings.FirstOrDefaultAsync(a =>
            a.StoreId == StoreId && a.Key == DbSettingKeys.ExchangeOrderSettings.ToString());
        if (dbSetting != null)
        {
            dbSetting.Value = JsonConvert.SerializeObject(model);
        }
        else
        {
            // Add a new setting since it does not exist
            var newSetting = new DbSetting
            {
                Key = DbSettingKeys.ExchangeOrderSettings.ToString(),
                StoreId = StoreId,
                Value = JsonConvert.SerializeObject(model)
            };
            db.Settings.Add(newSetting);
        }

        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index), new { StoreId });
    }
}