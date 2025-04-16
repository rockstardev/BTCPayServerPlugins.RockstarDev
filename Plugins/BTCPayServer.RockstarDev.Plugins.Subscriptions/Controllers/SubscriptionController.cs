using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.RockstarDev.Plugins.Subscriptions.Data;
using BTCPayServer.RockstarDev.Plugins.Subscriptions.Data.Models;
using BTCPayServer.RockstarDev.Plugins.Subscriptions.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.FileIO;
using MimeKit;

namespace BTCPayServer.RockstarDev.Plugins.Subscriptions.Controllers;

[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Route("~/plugins/{storeId}/subscriptions/")]
public class SubscriptionController : Controller
{
    private readonly PluginDbContext _dbContext;
    private readonly EmailService _emailService;

    public SubscriptionController(PluginDbContext dbContext, EmailService emailService)
    {
        _dbContext = dbContext;
        _emailService = emailService;
    }

    [FromRoute]
    public string StoreId { get; set; }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var subscriptions = await _dbContext.Subscriptions
            .Include(s => s.Customer)
            .Include(s => s.Product)
            .Where(s => s.Customer.StoreId == StoreId)
            .OrderByDescending(a => a.Expires)
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
        var product =
            await _dbContext.Products.FirstOrDefaultAsync(p => p.Name == "Bitcoin Magazine 12-month Subscription");
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
                //FormId = nameof(FormDataService.StaticFormEmailAddress),
                FormId = "Address",
                StoreId = storeId
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

        var isFirstLine = true;
        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields();
            if (isFirstLine)
            {
                isFirstLine = false;
                continue;
            } // Skip header

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
        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Severity = StatusMessageModel.StatusSeverity.Success, Message = subscriptionList.Count + " Subscriptions imported successfully!"
        });
        return RedirectToAction("Index", new { storeId = StoreId });
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

        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Severity = StatusMessageModel.StatusSeverity.Success, Message = "All subscriptions, products, and customers have been removed."
        });
        return RedirectToAction("Index", new { storeId = StoreId });
    }

    [HttpGet("SendReminders")]
    public async Task<IActionResult> SendReminders(string? subscriptionId)
    {
        var model = new SendReminderViewModel
        {
            RemindersList = new List<SelectListItem>
            {
                new("Subscription Expiring in 30 days", "30"),
                new("Subscription Expiring in 7 days", "7"),
                new("Subscription Expiring in 1 day", "1"),
                new("Expired Subscriptions", "0")
            },
            Subject = "Your subscription is expiring",
            Body = @"Dear {CustomerName},

Your {ProductName} subscription is expiring on {ExpiryDate}. Please renew your subscription by following the link below:
{RenewalLink}

Thank you,
{StoreName}"
        };

        if (!string.IsNullOrEmpty(subscriptionId))
        {
            var existingSubscription = await _dbContext.Subscriptions
                .Include(a => a.Customer)
                .SingleOrDefaultAsync(a => a.Id == subscriptionId);
            if (existingSubscription != null)
            {
                var customer = existingSubscription.Customer;

                model.ReminderType = "-1";
                model.SubscriptionId = subscriptionId;
                model.SubscriptionCustomer = $"{customer.Name} <{customer.Email}>";
            }
        }

        return View(model);
    }

    [HttpPost("SendReminders")]
    public async Task<IActionResult> SendReminders(SendReminderViewModel model)
    {
        var subscriptions = new List<Subscription>();
        if (model.SubscriptionId != null)
        {
            var s = _dbContext.Subscriptions
                .Include(a => a.Customer)
                .Include(a => a.Product)
                .Single(s => s.Id == model.SubscriptionId);
            subscriptions.Add(s);
        }
        else
        {
            var cutoffDate = DateTimeOffset.UtcNow.AddDays(int.Parse(model.ReminderType));

            subscriptions = await _dbContext.Subscriptions
                .Include(s => s.Customer)
                .Include(s => s.Product)
                .Where(s => s.Customer.StoreId == StoreId && s.Expires < cutoffDate)
                .ToListAsync();
        }

        var store = HttpContext.GetStoreData();
        foreach (var subscription in subscriptions)
        {
            var customer = subscription.Customer;
            var product = subscription.Product;

            var srid = Guid.NewGuid().ToString();
            var reminder = new SubscriptionReminder
            {
                Id = srid,
                SubscriptionId = subscription.Id,
                Created = DateTimeOffset.UtcNow
            };
            _dbContext.SubscriptionReminders.Add(reminder);
            await _dbContext.SaveChangesAsync();

            var renewalLink = Url.Action("Click", "Public", new { StoreId, srid }, Request.Scheme);

            var subject = model.Subject
                .Replace("{CustomerName}", customer.Name)
                .Replace("{StoreName}", store.StoreName);

            var body = model.Body
                .Replace("{CustomerName}", customer.Name)
                .Replace("{ProductName}", product.Name)
                .Replace("{ExpiryDate}", subscription.Expires.ToString("MMM dd, yyyy h:mm tt zzz"))
                .Replace("{StoreName}", store.StoreName)
                .Replace("{RenewalLink}", renewalLink);

            // Send email
            var email = new EmailService.EmailRecipient
            {
                Address = InternetAddress.Parse(customer.Email),
                Subject = subject,
                MessageText = body
            };
            await _emailService.SendEmail(StoreId, email);
        }

        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Severity = StatusMessageModel.StatusSeverity.Success, Message = subscriptions.Count + " subscription reminders have been sent"
        });
        return RedirectToAction(nameof(Index), new { storeId = StoreId });
    }

    public class SubscriptionsImportViewModel
    {
        [DisplayName("CSV File with Subscriptions")]
        [Required]
        public IFormFile CsvFile { get; set; }
    }


    // send reminders
    public class SendReminderViewModel
    {
        public string? ReminderType { get; set; }
        public IEnumerable<SelectListItem> RemindersList { get; set; }
        public string Subject { get; set; }
        public string Body { get; set; }
        public string? SubscriptionId { get; set; }
        public string? SubscriptionCustomer { get; set; }
    }
}
