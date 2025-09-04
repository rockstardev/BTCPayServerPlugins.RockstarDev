using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.RockstarDev.Plugins.MarkPaidCheckout.Server;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.RockstarDev.Plugins.MarkPaidCheckout.Controllers;

[Route("server/markpaid")]
[Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class MarkPaidServerController(SettingsRepository settings) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var s = await settings.GetSettingAsync<MarkPaidSettings>(MarkPaidCheckoutPlugin.SettingKey) ?? new MarkPaidSettings();
        return View("Views/MarkPaid/ServerConfig", s);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(MarkPaidSettings model)
    {
        model.MethodsCsv ??= "CASH";
        await settings.UpdateSetting(model, MarkPaidCheckoutPlugin.SettingKey);
        TempData["StatusMessage"] = "Settings saved. Please restart the server to apply changes.";
        TempData["RestartRequired"] = "1";
        return RedirectToAction(nameof(Index));
    }
}
