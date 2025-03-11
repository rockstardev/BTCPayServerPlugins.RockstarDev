using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.HostedServices.Webhooks;
using BTCPayServer.RockstarDev.Plugins.Subscriptions.Data;
using BTCPayServer.RockstarDev.Plugins.Subscriptions.Data.Models;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.PaymentRequests;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MimeKit;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PaymentRequestData = BTCPayServer.Client.Models.PaymentRequestData;
using WebhookDeliveryData = BTCPayServer.Data.WebhookDeliveryData;

namespace BTCPayServer.RockstarDev.Plugins.Subscriptions.Services;

public class SubscriptionService(
    EventAggregator eventAggregator,
    ILogger<SubscriptionService> logger,
    PluginDbContext dbContext,
    PaymentRequestRepository paymentRequestRepository,
    EmailService emailService)
    : EventHostedServiceBase(eventAggregator, logger), IPeriodicTask
{
    protected override void SubscribeToEvents()
    {
        Subscribe<PaymentRequestEvent>();
        base.SubscribeToEvents();
    }
    
    public Task Do(CancellationToken cancellationToken)
    {
        // TODO: Implement period check whether subscriptions expired and send reminders / update statuses
        return Task.CompletedTask;
    }

    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        switch (evt)
        {
            case PaymentRequestEvent { Type: PaymentRequestEvent.StatusChanged } payreq:
            {
                if (payreq.Data.Status == PaymentRequestData.PaymentRequestStatus.Completed)
                {
                    var subscriptionReminder = await dbContext.SubscriptionReminders
                        .Include(a => a.Subscription)
                        .Include(a => a.Subscription.Product)
                        .Include(a => a.Subscription.Customer)
                        .SingleOrDefaultAsync(a => a.PaymentRequestId == payreq.Data.Id, cancellationToken);

                    if (subscriptionReminder != null)
                    {
                        // var blob = payreq.Data.GetBlob();
                        // var email = blob.Email ?? blob.FormResponse?["buyerEmail"]?.Value<string>();
                        // await HandlePaidSubscription(subscriptionAppId, subscriptionId, payreq.Data.Id,
                        //     email);
                        
                        // TODO: Update customer if needed from blob.FormResponse
                        var sub = subscriptionReminder.Subscription;
                        if (sub.Product.DurationType == DurationTypes.Day)
                            sub.Expires = sub.Expires.AddDays(sub.Product.Duration);
                        else if (sub.Product.DurationType == DurationTypes.Month)
                            sub.Expires = sub.Expires.AddMonths(sub.Product.Duration);
                        
                        dbContext.Subscriptions.Update(sub);
                        await dbContext.SaveChangesAsync(cancellationToken);
                    }
                }

                break;
            }
        }

        await base.ProcessEvent(evt, cancellationToken);
    }

    private async Task ProcessSubscriptionReminders()
    {
        return; // TODO: Remove this line

        // TODO: This method is completely wrong, needs to be rewritten
        // Like fetch all the products... see what kind of reminders are there
        // Then fetch all the subscriptions that are about to expire based on this criteria
        // And then ensure that we don't send reminders for the same subscription multiple times
        // Will likely need to extend SubscriptionReminder.cs to have a field that indicates what kind of reminder is sent

        var cutoff = DateTimeOffset.UtcNow.AddMonths(1);

        // Get subscriptions that expire within the next month and have no reminders set
        // var subscriptions = await dbContext.Subscriptions
        //     .Include(s => s.Customer)
        //     .Include(s => s.Product)
        //     .Include(s => s.SubscriptionReminders)
        //     .Where(s => s.Expires < cutoff && !s.SubscriptionReminders.Any())
        //     .ToListAsync();
        //
        // foreach (var subscription in subscriptions)
        // {
        //     try
        //     {
        //         // Parse the ReminderDays from Product
        //         var reminderDays = subscription.Product.ReminderDays
        //             .Split(',')
        //             .Select(d => int.TryParse(d.Trim(), out var day) ? day : (int?)null)
        //             .Where(d => d.HasValue)
        //             .Select(d => d.Value)
        //             .Distinct()
        //             .ToList();
        //
        //         if (!reminderDays.Any())
        //         {
        //             logger.LogInformation(
        //                 $"No valid reminder days configured for Product {subscription.Product.Id}, skipping.");
        //             continue;
        //         }
        //
        //         var newReminders = new List<SubscriptionReminder>();
        //         foreach (var daysBefore in reminderDays)
        //         {
        //             var reminderDate = subscription.Expires.AddDays(-daysBefore);
        //
        //             if (reminderDate <= DateTimeOffset.UtcNow)
        //                 continue; // Skip reminders that would be in the past
        //
        //             newReminders.Add(new SubscriptionReminder
        //             {
        //                 SubscriptionId = subscription.Id,
        //                 Created = DateTimeOffset.Now
        //             });
        //         }
        //
        //         if (!newReminders.Any())
        //             continue; // No valid reminders to create
        //
        //         dbContext.SubscriptionReminders.AddRange(newReminders);
        //         await dbContext.SaveChangesAsync();
        //
        //         // Create Payment Request only for the earliest reminder
        //         var firstReminder = newReminders.OrderBy(r => r.Created).FirstOrDefault();
        //         if (firstReminder != null)
        //         {
        //             var paymentRequest = await CreatePaymentRequest(subscription);
        //             if (paymentRequest == null)
        //             {
        //                 logger.LogWarning($"Failed to create Payment Request for Subscription {subscription.Id}");
        //                 continue;
        //             }
        //
        //             // Send Reminder Email
        //             await SendReminderEmail(subscription, paymentRequest.Id);
        //
        //             // Mark only this reminder as processed
        //             // firstReminder.PaymentRequestCreated = true;
        //             dbContext.SubscriptionReminders.Update(firstReminder);
        //             await dbContext.SaveChangesAsync();
        //         }
        //     }
        //     catch (Exception ex)
        //     {
        //         logger.LogError(ex, $"Error processing subscription {subscription.Id}");
        //     }
        // }
    }

    // private async Task<PaymentRequestData?> CreatePaymentRequest(Subscription subscription)
    // {
    //     var product = subscription.Product;
    //
    //     var paymentRequest = new PaymentRequestData
    //     {
    //         StoreDataId = subscription.Customer.StoreId,
    //         Status = Client.Models.PaymentRequestData.PaymentRequestStatus.Pending,
    //         Created = DateTimeOffset.UtcNow,
    //         Archived = false,
    //     };
    //
    //     var additionalData = new Dictionary<string, JToken>
    //     {
    //         { "subscriptionId", subscription.Id },
    //         { "source", "subscription" },
    //         { "productId", product.Id }
    //     };
    //
    //     paymentRequest.SetBlob(new PaymentRequestBaseData
    //     {
    //         ExpiryDate = subscription.Expires,
    //         Amount = product.Price,
    //         Currency = product.Currency,
    //         StoreId = subscription.Customer.StoreId,
    //         Title = $"Renewal for {product.Name}",
    //         Description = $"Subscription renewal reminder for {product.Name}.",
    //         AdditionalData = additionalData
    //     });
    //
    //     var createdRequest = await paymentRequestRepository.CreateOrUpdatePaymentRequest(paymentRequest);
    //     if (createdRequest != null)
    //     {
    //         subscription.PaymentRequestId = createdRequest.Id;
    //         dbContext.Subscriptions.Update(subscription);
    //         await dbContext.SaveChangesAsync();
    //     }
    //
    //     return createdRequest;
    // }
    //
    // private async Task SendReminderEmail(Subscription subscription, string paymentRequestId)
    // {
    //     var email = subscription.Customer.Email;
    //     if (string.IsNullOrEmpty(email))
    //     {
    //         logger.LogWarning($"Subscription {subscription.Id} has no customer email.");
    //         return;
    //     }
    //
    //     var subject = $"Renewal Reminder for {subscription.Product.Name}";
    //     var body = $@"
    //         <p>Dear {subscription.Customer.Name},</p>
    //         <p>Your subscription for <strong>{subscription.Product.Name}</strong> is set to expire on {subscription.Expires:yyyy-MM-dd}.</p>
    //         <p><a href='/payment-requests/{paymentRequestId}'>Click here to renew now</a>.</p>
    //         <p>Thank you for your continued support!</p>";
    //
    //     var request = new EmailService.EmailRecipient
    //     {
    //         Address = InternetAddress.Parse(email),
    //         MessageText = body,
    //         Subject = subject
    //     };
    //     await emailService.SendEmail(subscription.Customer.StoreId, request);
    // }
}
