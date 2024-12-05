using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stripe;

namespace BTCPayServer.RockstarDev.Plugins.Stripe.Controllers;

[Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class StripeController(
    StripeService stripeService) : Controller
{
    [HttpGet("~/plugins/stripe/payouts")]
    public async Task<IActionResult> Payouts()
    {
        var payouts = await stripeService.GetAllPayoutsAsync();
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

public class StripeService
{
    public async Task<List<Payout>> GetAllPayoutsAsync()
    {
        var payouts = new PayoutService();
        var allPayouts = await payouts.ListAsync();
        return allPayouts.Data;
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