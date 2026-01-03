using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Payments;
using BTCPayServer.RockstarDev.Plugins.MarkPaidCheckout.PaymentHandlers;
using BTCPayServer.RockstarDev.Plugins.MarkPaidCheckout.Server;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
        // Normalize: trim and handle empty/null (empty is valid - disables all methods)
        var input = (model.MethodsCsv ?? string.Empty).Trim();

        // Parse, normalize to uppercase (matching plugin startup behavior), and deduplicate
        var tokens = string.IsNullOrWhiteSpace(input)
            ? new List<string>()
            : input
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim().ToUpperInvariant())
                .Where(t => !string.IsNullOrEmpty(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        var invalid = new List<string>();
        var reserved = new List<string>();

        foreach (var token in tokens)
        {
            // Validate format: alphanumeric, hyphens, underscores only
            if (!IsValidMethodName(token))
            {
                invalid.Add(token);
                continue;
            }

            // Check if this token would conflict with an external payment method
            // Note: We normalize to uppercase to match plugin startup behavior (line 48 in plugin)
            if (PaymentMethodId.TryParse(token, out var pmi) && pmi is not null)
                // Block any method that has a registered handler that's NOT from this plugin
                if (paymentHandlers.TryGetValue(pmi, out var handler) && handler is not MarkPaidPaymentMethodHandler)
                    reserved.Add(token);
        }

        if (invalid.Count > 0)
        {
            ModelState.AddModelError(nameof(MarkPaidSettings.MethodsCsv),
                $"Invalid method names (use only letters, numbers, hyphens, underscores): {string.Join(", ", invalid)}");
            return View("Views/MarkPaid/ServerConfig", model);
        }

        if (reserved.Count > 0)
        {
            ModelState.AddModelError(nameof(MarkPaidSettings.MethodsCsv),
                $"These method keys are already registered by BTCPay or another plugin: {string.Join(", ", reserved)}");
            return View("Views/MarkPaid/ServerConfig", model);
        }

        // Save normalized uppercase CSV (empty string if no methods)
        model.MethodsCsv = tokens.Count > 0 ? string.Join(",", tokens) : string.Empty;
        await settings.UpdateSetting(model, MarkPaidCheckoutPlugin.SettingKey);
        TempData["StatusMessage"] = "Settings saved. Please restart the server to apply changes.";
        TempData["RestartRequired"] = "1";
        return RedirectToAction(nameof(Index));
    }

    private static bool IsValidMethodName(string name)
    {
        // Allow alphanumeric, hyphens, underscores. No spaces or special chars.
        return !string.IsNullOrWhiteSpace(name) && Regex.IsMatch(name, @"^[a-zA-Z0-9_-]+$");
    }
}
