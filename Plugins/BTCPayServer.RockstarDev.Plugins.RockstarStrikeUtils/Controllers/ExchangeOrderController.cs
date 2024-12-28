using System.Threading.Tasks;
using BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Data;
using BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Logic;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Controllers;

public class ExchangeOrderController(
    RockstarStrikeDbContextFactory strikeDbContextFactory,
    StrikeClientFactory strikeClientFactory) : Controller
{
    [HttpGet("~/plugins/stripe/index")]
    public async Task<IActionResult> Index()
    {
        var isSetup = await strikeClientFactory.ClientExistsAsync();
        return RedirectToAction(isSetup ? nameof(Payouts) : nameof(Configuration));
    }
}