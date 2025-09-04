using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.RockstarDev.Plugins.MarkPaidCheckout.Server;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Linq;
using System.Collections.Generic;
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Payments.LNURLPay;

namespace BTCPayServer.RockstarDev.Plugins.MarkPaidCheckout.Controllers;

[Route("server/markpaid")]
[Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class MarkPaidServerController(SettingsRepository settings, PaymentMethodHandlerDictionary paymentHandlers) : Controller
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

        // Validate that none of the provided keys conflict with built-in BTCPay payment methods
        var tokens = (model.MethodsCsv ?? string.Empty)
            .Split(',', System.StringSplitOptions.RemoveEmptyEntries)
            .Select(t => (t ?? string.Empty).Trim())
            .Where(t => !string.IsNullOrEmpty(t))
            .ToList();

        var reserved = new List<string>();
        foreach (var token in tokens)
        {
            if (!PaymentMethodId.TryParse(token, out var pmi) || pmi is null)
                continue;
            if (paymentHandlers.TryGetValue(pmi, out var handler) && IsBuiltIn(handler))
            {
                reserved.Add(token);
            }
        }

        if (reserved.Count > 0)
        {
            var unique = reserved.Distinct(System.StringComparer.OrdinalIgnoreCase).ToArray();
            ModelState.AddModelError(nameof(MarkPaidSettings.MethodsCsv),
                $"These method keys are reserved by BTCPay and cannot be used: {string.Join(", ", unique)}");
            return View("Views/MarkPaid/ServerConfig", model);
        }

        await settings.UpdateSetting(model, MarkPaidCheckoutPlugin.SettingKey);
        TempData["StatusMessage"] = "Settings saved. Please restart the server to apply changes.";
        TempData["RestartRequired"] = "1";
        return RedirectToAction(nameof(Index));
    }

    private static bool IsBuiltIn(IPaymentMethodHandler handler) =>
        handler is BitcoinLikePaymentHandler ||
        handler is LightningLikePaymentHandler ||
        handler is LNURLPayPaymentHandler;
}
