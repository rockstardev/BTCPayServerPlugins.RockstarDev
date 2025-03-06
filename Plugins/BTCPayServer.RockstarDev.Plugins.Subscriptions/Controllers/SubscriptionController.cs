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
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.VisualBasic.FileIO;

namespace BTCPayServer.RockstarDev.Plugins.Subscriptions.Controllers;

[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Route("~/plugins/{storeId}/subscriptions/")]
public class SubscriptionController : Controller
{
    private readonly PluginDbContext _dbContext;

    public SubscriptionController(PluginDbContext dbContext)
    {
        _dbContext = dbContext;
    }
    
    [FromRoute] public string StoreId { get; set; }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var subscriptions = await _dbContext.Subscriptions
            .Include(s => s.Customer)
            .Include(s => s.Product)
            .Where(s => s.Customer.StoreId == StoreId)
            .ToListAsync();

        return View(subscriptions);
    }

    [HttpGet("create")]
    public async Task<IActionResult> Create()
    {
        ViewBag.Customers = await _dbContext.Customers
            .Where(c => c.StoreId == StoreId)
            .ToListAsync();

        ViewBag.Products = await _dbContext.Products
            .Where(p => p.StoreId == StoreId)
            .ToListAsync();

        return View(new Subscription());
    }

    [HttpPost("create")]
    public async Task<IActionResult> Create(Subscription subscription)
    {
        if (!ModelState.IsValid)
            return View(subscription);

        subscription.Created = DateTimeOffset.UtcNow;
        subscription.State = SubscriptionStates.Active;

        _dbContext.Subscriptions.Add(subscription);
        await _dbContext.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

    [HttpGet("edit/{id}")]
    public async Task<IActionResult> Edit(string id)
    {
        var subscription = await _dbContext.Subscriptions
            .Where(s => s.Customer.StoreId == StoreId && s.Id == id)
            .FirstOrDefaultAsync();

        if (subscription == null)
            return NotFound();

        ViewBag.Customers = await _dbContext.Customers
            .Where(c => c.StoreId == StoreId)
            .ToListAsync();

        ViewBag.Products = await _dbContext.Products
            .Where(p => p.StoreId == StoreId)
            .ToListAsync();

        return View(subscription);
    }

    [HttpPost("edit/{id}")]
    public async Task<IActionResult> Edit(string id, Subscription subscription)
    {
        if (!ModelState.IsValid)
            return View(subscription);

        var existingSubscription = await _dbContext.Subscriptions
            .Where(s => s.Customer.StoreId == StoreId && s.Id == id)
            .FirstOrDefaultAsync();

        if (existingSubscription == null)
            return NotFound();

        existingSubscription.ProductId = subscription.ProductId;
        existingSubscription.CustomerId = subscription.CustomerId;
        existingSubscription.Expires = subscription.Expires;
        existingSubscription.State = subscription.State;
        existingSubscription.PaymentRequestId = subscription.PaymentRequestId;

        await _dbContext.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("delete/{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var subscription = await _dbContext.Subscriptions
            .Where(s => s.Customer.StoreId == StoreId && s.Id == id)
            .FirstOrDefaultAsync();

        if (subscription == null)
            return NotFound();

        _dbContext.Subscriptions.Remove(subscription);
        await _dbContext.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
    

    [HttpGet("import")]
    public IActionResult Import()
    {
        return View(new SubscriptionsImportViewModel());
    }

    public class SubscriptionsImportViewModel
    {
        [DisplayName("CSV File with Subscriptions")]
        [Required]
        public IFormFile CsvFile { get; set; }
    }
    
    [HttpPost("import")]
    public async Task<IActionResult> Import(SubscriptionsImportViewModel model)
    {
        if (model.CsvFile == null || model.CsvFile.Length == 0)
        {
            TempData["Error"] = "Please upload a valid CSV file.";
            return View(model);
        }

        var storeId = StoreId; // Adjust to actual store retrieval logic

        // Ensure the product exists5
        var product = await _dbContext.Products.FirstOrDefaultAsync(p => p.Name == "Bitcoin Magazine 12-month Subscription");
        if (product == null)
        {
            product = new Product
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Bitcoin Magazine 12-month Subscription",
                Price = 120m,
                Currency = "USD",
                Duration = 12,
                DurationType = DurationTypes.Month,
                ReminderDays = "30,7,1",
                StoreId = storeId,
            };
            _dbContext.Products.Add(product);
            await _dbContext.SaveChangesAsync();
        }

        using var reader = new StreamReader(model.CsvFile.OpenReadStream());
        var lineNumber = 0;
        
        using var parser = new TextFieldParser(reader);
        parser.TextFieldType = FieldType.Delimited;
        parser.SetDelimiters(",");

        var subscriptionList = new List<Subscription>();
        
        bool isFirstLine = true;
        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields();
            if (isFirstLine) { isFirstLine = false; continue; } // Skip header

            if (fields.Length < 9) continue; // Ensure correct format

            var email = fields[1].Trim();
            var firstName = fields[3].Trim();
            var lastName = fields[4].Trim();
            var address1 = fields[5].Trim();
            var address2 = fields[6].Trim();
            var country = fields[7].Trim();
            var zip = fields[8].Trim();
            var expiresAt = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(fields[13].Trim()));

            // Check if customer exists
            var customer = await _dbContext.Customers.FirstOrDefaultAsync(c => c.Email == email);
            if (customer == null)
            {
                customer = new Customer
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = $"{firstName} {lastName}",
                    Email = email,
                    Address1 = address1,
                    Address2 = address2,
                    City = "Foo",
                    Country = country,
                    ZipCode = zip,
                    StoreId = storeId
                };

                _dbContext.Customers.Add(customer);
                await _dbContext.SaveChangesAsync();
            }

            // Check if subscription already exists
            var existingSubscription = await _dbContext.Subscriptions
                .FirstOrDefaultAsync(s => s.CustomerId == customer.Id && s.ProductId == product.Id);

            if (existingSubscription != null) continue;

            // Create subscription
            var subscription = new Subscription
            {
                Id = Guid.NewGuid().ToString(),
                CustomerId = customer.Id,
                ProductId = product.Id,
                Created = DateTimeOffset.UtcNow,
                Expires = expiresAt,
                State = expiresAt > DateTimeOffset.UtcNow ? SubscriptionStates.Active : SubscriptionStates.Expired,
                ExternalId = "shopify-1234",
                PaymentRequestId = "pending"
            };

            subscriptionList.Add(subscription);
        }

        _dbContext.Subscriptions.AddRange(subscriptionList);
        await _dbContext.SaveChangesAsync();
        TempData.SetStatusMessageModel(new StatusMessageModel()
        {
            Severity = StatusMessageModel.StatusSeverity.Success,
            Message = subscriptionList.Count + " Subscriptions imported successfully!"
        });
        return RedirectToAction("Index", new { storeId = StoreId});
    }


    [HttpPost("ClearSubscriptions")]
    public async Task<IActionResult> ClearSubscriptions()
    {
        var subscriptions = await _dbContext.Subscriptions
            .Where(s => s.Customer.StoreId == StoreId)
            .ToListAsync();

        var customerIds = subscriptions.Select(s => s.CustomerId).Distinct().ToList();
        var productIds = subscriptions.Select(s => s.ProductId).Distinct().ToList();

        _dbContext.Subscriptions.RemoveRange(subscriptions);

        var customers = await _dbContext.Customers
            .Where(c => customerIds.Contains(c.Id))
            .ToListAsync();

        var products = await _dbContext.Products
            .Where(p => productIds.Contains(p.Id))
            .ToListAsync();

        _dbContext.Customers.RemoveRange(customers);
        _dbContext.Products.RemoveRange(products);

        await _dbContext.SaveChangesAsync();

        TempData.SetStatusMessageModel(new StatusMessageModel()
        {
            Severity = StatusMessageModel.StatusSeverity.Success,
            Message = "All subscriptions, products, and customers have been removed."
        });
        return RedirectToAction("Index", new { storeId = StoreId});
    }

    
    // send reminders
    public class SendReminderViewModel
    {
        public string ReminderType { get; set; }
        public IEnumerable<SelectListItem> RemindersList { get; set; }
        public string Subject { get; set; }
        public string Body { get; set; }
    }

    [HttpGet("SendReminders")]
    public async Task<IActionResult> SendReminders()
    {
        var model = new SendReminderViewModel
        {
            RemindersList = new List<SelectListItem>
            {
                new SelectListItem("Subscription Expiring in 30 days", "30"),
                new SelectListItem("Subscription Expiring in 7 days", "7"),
                new SelectListItem("Subscription Expiring in 1 day", "1"),
                new SelectListItem("Expired Subscriptions", "0")
            },
            Subject = "Your subscription is about to expire",
            Body = @"Dear {CustomerName},

Your subscription is about to expire. Please renew it to continue receiving our product.

{RenewalLink}

Thank you,
{StoreName}"
        };
        
        return View(model);
    }

    [HttpPost("SendReminders")]
    public async Task<IActionResult> SendReminders(SendReminderViewModel model)
    {
        var cutoffDate = DateTimeOffset.UtcNow.AddDays(int.Parse(model.ReminderType));
        var subscriptions = await _dbContext.Subscriptions
            .Include(s => s.Customer)
            .Include(s => s.Product)
            .Where(s => s.Customer.StoreId == StoreId && s.Expires < cutoffDate)
            .ToListAsync();

        TempData.SetStatusMessageModel(new StatusMessageModel()
        {
            Severity = StatusMessageModel.StatusSeverity.Success,
            Message = subscriptions.Count + " subscription reminders have been sent"
        });
        return RedirectToAction(nameof(Index), new { storeId = StoreId });
    }
}
