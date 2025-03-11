using System;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using BTCPayServer.RockstarDev.Plugins.Subscriptions.Data;
using BTCPayServer.RockstarDev.Plugins.Subscriptions.Data.Models;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Services.PaymentRequests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.VisualBasic.FileIO;
using Newtonsoft.Json.Linq;

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
    
    [FromRoute] public string StoreId { get; set; }

    [HttpGet("click/{srid}")]
    public async Task<IActionResult> Click(string srid)
    {
        var reminder = await _dbContext.SubscriptionReminders.FindAsync(srid);
        if (reminder == null)
        {
            return NotFound();
        }

        reminder = await _dbContext.SubscriptionReminders
            .Include(a => a.Subscription)
            .Include(a => a.Subscription.Product)
            .Include(a => a.Subscription.Customer)
            .SingleAsync(pr => pr.Id == srid);

        var product = reminder.Subscription.Product;
        if (String.IsNullOrEmpty(reminder.PaymentRequestId))
        {
            var req = new BTCPayServer.Data.PaymentRequestData()
            {
                StoreDataId = product.StoreId,
                Archived = false,
                Status = Client.Models.PaymentRequestData.PaymentRequestStatus.Pending
            };
            req.SetBlob(new CreatePaymentRequestRequest
            {
                Amount = product.Price,
                Currency = product.Currency,
                ExpiryDate = null,
                Description = "",
                Title = product.Name + " Renewal",
                //FormId = "Address",
                AllowCustomPaymentAmounts = false,
                AdditionalData = new Dictionary<string, JToken>()
                {
                    { "source", JToken.FromObject("subscription") },
                    //{ "appId", JToken.FromObject(appId) },
                    { "url", HttpContext.Request.GetAbsoluteRoot() }
                },
            });

            var pr = await _paymentRequestRepository.CreateOrUpdatePaymentRequest(req);
            reminder.PaymentRequestId = pr.Id;
            
            _dbContext.SubscriptionReminders.Update(reminder);
            await _dbContext.SaveChangesAsync();
        }
        
        return RedirectToAction("ViewPaymentRequest", "UIPaymentRequest", new { payReqId = reminder.PaymentRequestId });
    }
}
