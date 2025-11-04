using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.RockstarDev.Plugins.WalletSweeper.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.RockstarDev.Plugins.WalletSweeper.Controllers;

[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Route("~/plugins/{storeId}/walletsweeper")]
public class WalletSweeperController(
    PluginDbContextFactory dbContextFactory) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string storeId)
    {
        await using var db = dbContextFactory.CreateContext();
        
        var configs = await db.SweepConfigurations
            .Where(c => c.StoreId == storeId)
            .OrderBy(c => c.ConfigName)
            .ToListAsync();

        return View(configs);
    }
}
