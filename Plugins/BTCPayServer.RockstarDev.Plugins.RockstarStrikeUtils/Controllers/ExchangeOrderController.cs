using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Data;
using BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Data.Models;
using BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Logic;
using BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.ViewModels.ExchangeOrder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Controllers;

[Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Route("~/plugins/{storeId}/rockstarstrike/exchangeorder")]
public class ExchangeOrderController(
    RockstarStrikeDbContextFactory strikeDbContextFactory,
    StrikeClientFactory strikeClientFactory) : Controller
{
    [FromRoute]
    public string StoreId { get; set; }
    
    [HttpGet("index")]
    public async Task<IActionResult> Index()
    {
        await using var db = strikeDbContextFactory.CreateContext();
        var list = db.ExchangeOrders.Where(a=>a.StoreId == StoreId).ToList();
        var viewModel = new IndexViewModel
        {
            List = list
        };
        return View(viewModel);
    }

    [HttpGet("create")]
    public IActionResult Create()
    {
        // Return a blank CreateExchangeOrderViewModel for the form
        var viewModel = new CreateExchangeOrderViewModel();
        return View(viewModel);
    }

    [HttpPost("create")]
    public async Task<IActionResult> Create([FromForm] CreateExchangeOrderViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model); // Return the same view with validation errors
        }

        await using var db = strikeDbContextFactory.CreateContext();
        var exchangeOrder = new DbExchangeOrder
        {
            StoreId = StoreId,
            Operation = model.Operation,
            Amount = model.Amount,
            DelayUntil = model.DelayUntil,
            Created = DateTime.UtcNow,
            State = DbExchangeOrder.States.Created,
            CreatedBy = "Manual"
        };
            
        db.ExchangeOrders.Add(exchangeOrder);
        await db.SaveChangesAsync();

        return RedirectToAction("Index");
    }
}