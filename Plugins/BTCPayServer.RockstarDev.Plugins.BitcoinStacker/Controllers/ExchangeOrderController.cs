using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Data;
using BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Data.Models;
using BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Logic;
using BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Services;
using BTCPayServer.RockstarDev.Plugins.BitcoinStacker.ViewModels.ExchangeOrder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Strike.Client;
using Strike.Client.Balances;
using Strike.Client.Models;

namespace BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Controllers;

[Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Route("~/plugins/{storeId}/exchangeorder")]
public class ExchangeOrderController(
    PluginDbContextFactory pluginDbContextFactory,
    StrikeClientFactory strikeClientFactory,
    EventAggregator eventAggregator) : Controller
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

        // Have the BTC balance on Strike in the database ready to fetch
        var balances = db.SettingFetch(StoreId, DbSettingKeys.StrikeBalances);
        if (balances != null)
        {
            var balancesViewModel = JsonConvert.DeserializeObject<ResponseCollection<Balance>>(balances.Value);
            viewModel.BTCBalance =
                balancesViewModel.FirstOrDefault(a => a.Currency == Currency.Btc)?.Total.ToString("N8");
            viewModel.USDBalance =
                balancesViewModel.FirstOrDefault(a => a.Currency == Currency.Usd)?.Total.ToString("N2");
        }

        var exchangeRates = db.SettingFetch(StoreId, DbSettingKeys.StrikeExchangeRates);
        if (exchangeRates != null)
        {
            var exchangeRatesViewModel = JsonConvert.DeserializeObject<ResponseCollection<ConversionAmount>>(exchangeRates.Value);
            var exchangeRate = exchangeRatesViewModel.FirstOrDefault(a =>
                a.SourceCurrency == Currency.Btc && a.TargetCurrency == Currency.Usd)?.Amount;

            if (exchangeRate.HasValue)
            {
                var totalCost = list.Sum(a => a.Amount);
                var totalBitcoin = list.Sum(a => a.TargetAmount);
                
                var totalBitcoinIsUsd = totalBitcoin ?? 0 * exchangeRate.Value;
                viewModel.ProfitUSD = (totalBitcoinIsUsd - totalCost).ToString("N2");
            }
        }

        return View(viewModel);
    }

    [HttpGet("IndexLogs")]
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

    // IndexLogs
    // TODO: Switch to HTTPPOST
    [HttpGet("SwitchState")]
    public async Task<IActionResult> SwitchState(Guid id, DbExchangeOrder.States state)
    {
        var db = pluginDbContextFactory.CreateContext();

        var order = db.ExchangeOrders.Single(a => a.Id == id);
        order.State = state;
        await db.SaveChangesAsync();

        TempData[WellKnownTempData.SuccessMessage] =
            $"State of Exchange Order {id} has been updated to {state}";

        return RedirectToAction(nameof(Index), new { StoreId });
    }


    [HttpPost("AddDelay")]
    public async Task<IActionResult> AddDelay(Guid id)
    {
        var db = pluginDbContextFactory.CreateContext();

        var order = db.ExchangeOrders.Single(a => a.Id == id);
        order.DelayUntil = ExchangeOrderHeartbeatService.DELAY_UNTIL;
        await db.SaveChangesAsync();

        TempData[WellKnownTempData.SuccessMessage] =
            $"Delay on Exchange Order {id} has been added";

        return RedirectToAction(nameof(Index), new { StoreId });
    }

    [HttpPost("ForceConversion")]
    public async Task<IActionResult> ForceConversion(Guid id)
    {
        await using var db = pluginDbContextFactory.CreateContext();
        var order = db.ExchangeOrders.Single(a => a.Id == id);

        // trimming to 2 decimal places
        order.Amount = Math.Truncate(order.Amount * 100) / 100;

        var store = db.SettingFetch(StoreId, DbSettingKeys.ExchangeOrderSettings);
        var settings = SettingsViewModel.FromDbSettings(store);

        var strikeClient = strikeClientFactory.InitClient(settings.StrikeApiKey);
        await ExchangeOrderHeartbeatService.ExecuteConversionOrder(db, order, strikeClient, CancellationToken.None);
        await ExchangeOrderHeartbeatService.UpdateStrikeCache(db, order.StoreId, strikeClient,
            CancellationToken.None);

        TempData[WellKnownTempData.SuccessMessage] = $"Exchange Order {id} has been forced";

        return RedirectToAction(nameof(Index), new { StoreId });
    }

    [HttpPost("RunHeartbeatNow")]
    public async Task<IActionResult> RunHeartbeatNow()
    {
        await using var db = pluginDbContextFactory.CreateContext();
        var store = db.SettingFetch(StoreId, DbSettingKeys.ExchangeOrderSettings);
        var settings = SettingsViewModel.FromDbSettings(store);

        eventAggregator.Publish(new ExchangeOrderHeartbeatService.PeriodProcessEvent { StoreId = StoreId, Setting = settings });
        TempData[WellKnownTempData.SuccessMessage] = "Heartbeat run in progress";

        return RedirectToAction(nameof(Index), new { StoreId });
    }

    [HttpPost("UpdateExchangeRates")]
    public async Task<IActionResult> UpdateExchangeRates()
    {
        await using var db = pluginDbContextFactory.CreateContext();

        var exchanges = db.ExchangeOrders
            .Where(a => a.StoreId == StoreId &&
                        a.ConversionRate != null && a.ConversionRate > 0 && a.ConversionRate < 0.01m)
            .ToList();

        foreach (var exchange in exchanges)
            if (exchange.ConversionRate != null)
                exchange.ConversionRate = Math.Round(1m / exchange.ConversionRate.Value, 2);

        await db.SaveChangesAsync();

        TempData[WellKnownTempData.SuccessMessage] = "Exchange Rates updated";

        return RedirectToAction(nameof(Index), new { StoreId });
    }


    //

    [HttpGet("create")]
    public IActionResult Create()
    {
        var viewModel = new CreateExchangeOrderViewModel();
        return View(viewModel);
    }

    [HttpPost("create")]
    public async Task<IActionResult> Create([FromForm] CreateExchangeOrderViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

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
        var dbSetting = db.SettingFetch(StoreId, DbSettingKeys.ExchangeOrderSettings);

        var viewModel = SettingsViewModel.FromDbSettings(dbSetting);
        return View(viewModel);
    }

    [HttpPost("settings")]
    public async Task<IActionResult> Settings([FromForm] SettingsViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        // automatically set up ACH deposit method
        if (!string.IsNullOrEmpty(model.StrikeApiKey) && model.StrikePaymentMethodId == Guid.Empty)
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
        var _ = db.SettingAddOrUpdate(StoreId, DbSettingKeys.ExchangeOrderSettings, model);

        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index), new { StoreId });
    }
}
