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

namespace BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Controllers;

[Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class RockstarStrikeUtilsController(RockstarStrikeDbContextFactory strikeDbContextFactory,
    StrikeClientFactory strikeClientFactory) : Controller
{
    [HttpGet("~/plugins/rockstarstrikeutils/index")]
    public async Task<IActionResult> Index()
    {
        return RedirectToAction(nameof(ReceiveRequests));
    }
    
    [HttpGet("~/plugins/rockstarstrikeutils/Configuration")]
    public async Task<IActionResult> Configuration()
    {
        await using var db = strikeDbContextFactory.CreateContext();

        var model = new DashboardViewModel
        {
            StrikeApiKey = db.Settings.SingleOrDefault(a => a.Key == "StrikeApiKey")?.Value
        };
        
        return View(model);
    }

    [HttpPost("~/plugins/rockstarstrikeutils/Configuration")]
    public async Task<IActionResult> Configuration(DashboardViewModel model)
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
        
        return View(model);
    }
}