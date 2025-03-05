using System;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using BTCPayServer.RockstarDev.Plugins.Subscriptions.Data;
using BTCPayServer.RockstarDev.Plugins.Subscriptions.Data.Models;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client;
using Microsoft.AspNetCore.Http;

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
    
    [HttpPost]
    public async Task<IActionResult> ImportSubscriptions(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            TempData["Error"] = "Please upload a valid CSV file.";
            return RedirectToAction("Index");
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

        using var reader = new StreamReader(file.OpenReadStream());
        var lineNumber = 0;
        
        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line) || lineNumber++ == 0) continue; // Skip header

            var fields = line.Split(',');

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
                State = SubscriptionStates.Active,
                ExternalId = "shopify-1234",
                PaymentRequestId = "pending"
            };

            _dbContext.Subscriptions.Add(subscription);
        }

        await _dbContext.SaveChangesAsync();
        TempData["Success"] = "Subscriptions imported successfully!";
        return RedirectToAction("Index", new {StoreId});
    }

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
}
