using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Data;
using BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Data.Models;
using BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.ViewModels.RockstarStrikeUtils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Strike.Client;

namespace BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Controllers;

[Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class RockstarStrikeUtilsController(RockstarStrikeDbContextFactory strikeDbContextFactory) : Controller
{
    [HttpGet("~/plugins/rockstarstrikeutils/index")]
    public async Task<IActionResult> Index()
    {
        return RedirectToAction(nameof(Dashboard));
    }
    
    [HttpGet("~/plugins/rockstarstrikeutils/dashboard")]
    public async Task<IActionResult> Dashboard()
    {
        await using var db = strikeDbContextFactory.CreateContext();

        var model = new DashboardViewModel
        {
            StrikeApiKey = db.Settings.SingleOrDefault(a => a.Key == "StrikeApiKey")?.Value
        };
        
        return View(model);
    }

    [HttpPost("~/plugins/rockstarstrikeutils/dashboard")]
    public async Task<IActionResult> Dashboard(DashboardViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        await using var db = strikeDbContextFactory.CreateContext();
        var setting = db.Settings.SingleOrDefault(a => a.Key == "StrikeApiKey");
        if (setting is null)
        {
            setting = new DbSetting
            {
                Key = "StrikeApiKey",
                Value = model.StrikeApiKey
            };
            db.Settings.Add(setting);
        }
        else
        {
            setting.Value = model.StrikeApiKey;
        }

        await db.SaveChangesAsync();
        
        return View(model);
    }
    
    [HttpGet("~/plugins/rockstarstrikeutils/receiverequests")]
    public async Task<IActionResult> ReceiveRequests()
    {
        var model = new ReceiveRequestsViewModel();
        
        return View(model);
    }
}