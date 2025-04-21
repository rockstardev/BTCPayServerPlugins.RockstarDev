using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.RockstarDev.Plugins.Subscriptions.Data;
using BTCPayServer.Services.PaymentRequests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using PaymentRequestData = BTCPayServer.Data.PaymentRequestData;

namespace BTCPayServer.RockstarDev.Plugins.Subscriptions.Controllers;

[AllowAnonymous]
[Route("~/plugins/{storeId}/subscriptions/public/")]
public class PublicController : Controller
{
    private readonly PluginDbContext _dbContext;
    private readonly PaymentRequestRepository _paymentRequestRepository;

    public PublicController(PluginDbContext dbContext, PaymentRequestRepository paymentRequestRepository)
    {
        _dbContext = dbContext;
        _paymentRequestRepository = paymentRequestRepository;
    }

    [FromRoute]
    public string StoreId { get; set; }

    [HttpGet("click/{srid}")]
    public async Task<IActionResult> Click(string srid)
    {
        var reminder = await _dbContext.SubscriptionReminders.FindAsync(srid);
        if (reminder == null) return NotFound();

        reminder = await _dbContext.SubscriptionReminders
            .Include(a => a.Subscription)
            .Include(a => a.Subscription.Product)
            .Include(a => a.Subscription.Customer)
            .SingleAsync(pr => pr.Id == srid);

        var product = reminder.Subscription.Product;
        if (string.IsNullOrEmpty(reminder.PaymentRequestId))
        {
            var req = new PaymentRequestData
            {
                StoreDataId = product.StoreId,
                Archived = false,
                Status = PaymentRequestStatus.Pending,
                Currency = product.Currency,
                Amount = product.Price,
            };
            req.SetBlob(new()
            {
                Description = "",
                Title = product.Name + " Renewal",
                FormId = product.FormId,
                AllowCustomPaymentAmounts = false,
                AdditionalData = new Dictionary<string, JToken>
                {
                    //{ "appId", JToken.FromObject(appId) },
                    { "source", JToken.FromObject("subscription") }, { "url", HttpContext.Request.GetAbsoluteRoot() }
                }
            });

            var pr = await _paymentRequestRepository.CreateOrUpdatePaymentRequest(req);
            reminder.PaymentRequestId = pr.Id;

            _dbContext.SubscriptionReminders.Update(reminder);
            await _dbContext.SaveChangesAsync();
        }

        return RedirectToAction("ViewPaymentRequest", "UIPaymentRequest", new { payReqId = reminder.PaymentRequestId });
    }
}
