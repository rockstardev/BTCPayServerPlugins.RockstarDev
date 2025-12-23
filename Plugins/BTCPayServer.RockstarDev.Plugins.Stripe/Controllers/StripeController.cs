using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.RockstarDev.Plugins.Stripe.Data;
using BTCPayServer.RockstarDev.Plugins.Stripe.Data.Models;
using BTCPayServer.RockstarDev.Plugins.Stripe.Logic;
using BTCPayServer.RockstarDev.Plugins.Stripe.ViewModels.Stripe;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.RockstarDev.Plugins.Stripe.Controllers;

[Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class StripeController(StripeClientFactory stripeClientFactory, StripeDbContextFactory stripeDbContextFactory)
    : Controller
{
    [HttpGet("~/plugins/stripe/index")]
    public async Task<IActionResult> Index()
    {
        var isSetup = await stripeClientFactory.ClientExistsAsync();
        return RedirectToAction(isSetup ? nameof(Payouts) : nameof(Configuration));
    }

    [HttpGet("~/plugins/stripe/Configuration")]
    public async Task<IActionResult> Configuration()
    {
        await using var db = stripeDbContextFactory.CreateContext();
        var model = new ConfigurationViewModel { StripeApiKey = db.Settings.SingleOrDefault(a => a.Key == DbSetting.StripeApiKey)?.Value };
        return View(model);
    }

    [HttpPost("~/plugins/stripe/Configuration")]
    public async Task<IActionResult> Configuration(ConfigurationViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var validKey = await stripeClientFactory.TestAndSaveApiKeyAsync(model.StripeApiKey);
        if (!validKey)
            ModelState.AddModelError(nameof(model.StripeApiKey), "Invalid API key.");
        else
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = "Strike API key saved successfully.", Severity = StatusMessageModel.StatusSeverity.Success
            });

        return View(model);
    }
     
    [HttpGet("~/plugins/stripe/payouts")]
    public async Task<IActionResult> Payouts(string startingAfter)
    {
        const string cookieKey = "PayoutsHistory";

        // Retrieve history from cookies
        var history = Request.Cookies[cookieKey] != null
            ? Request.Cookies[cookieKey].Split(',').ToList()
            : new List<string>();
        if (string.IsNullOrEmpty(startingAfter))
            history = new List<string>();

        var isGoingBack = !string.IsNullOrEmpty(startingAfter) && history.Any(a => a == startingAfter);

        if (isGoingBack)
        {
            // Remove the last entry when going back
            history.RemoveAt(history.Count - 1);
            startingAfter = history.LastOrDefault(); // Get the previous startingAfter
        }

        var payouts = await stripeClientFactory.PayoutsAllAsync(50, startingAfter);

        var model = new PayoutsViewModel
        {
            Payouts = payouts.Payouts.Select(p => new PayoutsViewModel.Item
            {
                PayoutId = p.Id,
                Created = p.Created,
                Amount = p.Amount / 100.0m, // Stripe uses cents
                Currency = p.Currency.ToUpper(),
                Status = p.Status,
                Method = p.Method,
                Description = p.Description
            }).ToList(),
            HasPrev = history.Any() ? history.LastOrDefault() : null,
            HasNext = payouts.HasNext ? payouts.Payouts.Last().Id : null
        };

        if (!isGoingBack && startingAfter != null)
        {
            // Append current startingAfter to history only if moving forward
            history.Add(startingAfter);
            if (history.Count == 1)
                model.HasPrev = "";
        }

        // Store updated history back in cookies
        Response.Cookies.Append(cookieKey, string.Join(",", history), new CookieOptions
        {
            HttpOnly = true,
            Secure = true, // Ensure secure transmission
            Expires = DateTime.UtcNow.AddHours(1) // Adjust expiration as needed
        });

        return View(model);
    }
}

public class PayoutsViewModel
{
    public List<Item> Payouts { get; set; } = new();
    public string HasPrev { get; set; }
    public string HasNext { get; set; }

    public class Item
    {
        public string PayoutId { get; set; }
        public DateTime Created { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; }
        public string Status { get; set; }
        public string Method { get; set; }
        public string Description { get; set; }
    }
}
