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
using Microsoft.AspNetCore.Mvc;
using Stripe;

namespace BTCPayServer.RockstarDev.Plugins.Stripe.Controllers;

[Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class StripeController(StripeClientFactory stripeClientFactory,
    StripeDbContextFactory stripeDbContextFactory) : Controller
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

            var model = new ConfigurationViewModel
            {
                StripeApiKey = db.Settings.SingleOrDefault(a => a.Key == DbSetting.StripeApiKey)?.Value
            };
        
            return View(model);
        }

        [HttpPost("~/plugins/stripe/Configuration")]
        public async Task<IActionResult> Configuration(ConfigurationViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var validKey = await stripeClientFactory.TestAndSaveApiKeyAsync(model.StripeApiKey);
            if (!validKey)
            {
                ModelState.AddModelError(nameof(model.StripeApiKey), "Invalid API key.");
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
        
    [HttpGet("~/plugins/stripe/payouts")]
    public async Task<IActionResult> Payouts()
    {
        var payouts = await stripeClientFactory.GetAllPayoutsAsync();
        var model = new PayoutsViewModel
        {
            Payouts = payouts.Select(p => new PayoutViewModel
            {
                PayoutId = p.Id,
                Created = p.Created,
                Amount = p.Amount / 100.0m, // Stripe uses cents
                Currency = p.Currency.ToUpper(),
                Status = p.Status,
                Method = p.Method,
                Description = p.Description
            }).ToList()
        };

        return View(model);
    }
}

public class PayoutsViewModel
{
    public List<PayoutViewModel> Payouts { get; set; } = new List<PayoutViewModel>();
}

public class PayoutViewModel
{
    public string PayoutId { get; set; }
    public DateTime Created { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; }
    public string Status { get; set; }
    public string Method { get; set; }
    public string Description { get; set; }
}