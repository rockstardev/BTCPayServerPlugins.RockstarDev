using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Data;
using BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Logic;
using BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.ViewModels.ExchangeOrder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Controllers;

[Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Route("~/plugins/rockstarstrike/exchangeorder")]
public class ExchangeOrderController(
    RockstarStrikeDbContextFactory strikeDbContextFactory,
    StrikeClientFactory strikeClientFactory) : Controller
{
    [HttpGet("index")]
    public async Task<IActionResult> Index()
    {
        await using var db = strikeDbContextFactory.CreateContext();
        var list = db.ExchangeOrders.ToList();
        var viewModel = new IndexViewModel
        {
            List = list
        };
        return View(viewModel);
    }
}