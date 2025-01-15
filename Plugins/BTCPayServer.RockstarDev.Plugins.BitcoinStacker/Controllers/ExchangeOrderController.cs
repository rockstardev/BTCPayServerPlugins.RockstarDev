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
    PluginDbContextFactory strikeDbContextFactory,
    StrikeClientFactory strikeClientFactory) : Controller
{
    [FromRoute]
    public string StoreId { get; set; }

    [HttpGet("index")]
    public async Task<IActionResult> Index()
    {
        await using var db = strikeDbContextFactory.CreateContext();
        var list = db.ExchangeOrders
            .Where(a => a.StoreId == StoreId)
            .OrderByDescending(a => a.CreatedForDate)
            .ThenByDescending(a => a.Created)
            .ToList();
        var viewModel = new IndexViewModel { List = list };
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
        var db = strikeDbContextFactory.CreateContext();

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

        await using var db = strikeDbContextFactory.CreateContext();
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
        await using var db = strikeDbContextFactory.CreateContext();
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

        await using var db = strikeDbContextFactory.CreateContext();
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