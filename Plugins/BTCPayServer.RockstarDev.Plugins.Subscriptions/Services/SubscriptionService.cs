using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.RockstarDev.Plugins.Subscriptions.Data;
using BTCPayServer.RockstarDev.Plugins.Subscriptions.Data.Models;
using BTCPayServer.Services.PaymentRequests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PaymentRequestData = BTCPayServer.Client.Models.PaymentRequestData;

namespace BTCPayServer.RockstarDev.Plugins.Subscriptions.Services;

public class SubscriptionService(
    EventAggregator eventAggregator,
    ILogger<SubscriptionService> logger,
    PluginDbContext dbContext,
    PaymentRequestRepository paymentRequestRepository,
    EmailService emailService)
    : EventHostedServiceBase(eventAggregator, logger), IPeriodicTask
{
    public Task Do(CancellationToken cancellationToken)
    {
        // TODO: Implement period check whether subscriptions expired and send reminders / update statuses
        return Task.CompletedTask;
    }

    protected override void SubscribeToEvents()
    {
        Subscribe<PaymentRequestEvent>();
        Subscribe<InvoiceEvent>();
        base.SubscribeToEvents();
    }

    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        switch (evt)
        {
            case InvoiceEvent ie:
            {
                if (ie.Invoice.Status == InvoiceStatus.Settled)
                {
                    var metadata = ie.Invoice.Metadata;
                    if (Guid.TryParse(metadata.ItemCode, out var guid))
                    {
                        var existingSubscription = await dbContext.Subscriptions.FirstOrDefaultAsync(a =>
                            a.ExternalId == ie.InvoiceId, cancellationToken);
                        if (existingSubscription != null)
                            break; // we already processed this invoice subscription

                        var product = await dbContext.Products.FindAsync(guid.ToString(), cancellationToken);
                        if (product != null)
                        {
                            // Check if customer exists
                            var customer =
                                await dbContext.Customers.FirstOrDefaultAsync(c => c.Email == metadata.BuyerEmail,
                                    cancellationToken);
                            if (customer == null)
                            {
                                customer = new Customer
                                {
                                    Id = Guid.NewGuid().ToString(),
                                    Email = metadata.BuyerEmail,
                                    StoreId = product.StoreId
                                };

                                dbContext.Customers.Add(customer);
                            }

                            customer.Name = metadata.BuyerName;
                            customer.Address1 = metadata.BuyerAddress1;
                            customer.Address2 = metadata.BuyerAddress2;
                            customer.City = metadata.BuyerCity;
                            customer.Country = metadata.BuyerCountry;
                            customer.ZipCode = metadata.BuyerZip;

                            // Create subscription
                            var subscription = new Subscription
                            {
                                Id = Guid.NewGuid().ToString(),
                                CustomerId = customer.Id,
                                ProductId = product.Id,
                                Created = DateTimeOffset.UtcNow,
                                Expires = DateTimeOffset.UtcNow.AddMonths(product.Duration),
                                State = SubscriptionStates.Active,
                                ExternalId = ie.InvoiceId,
                                PaymentRequestId = ""
                            };
                            dbContext.Subscriptions.Add(subscription);
                        }

                        await dbContext.SaveChangesAsync(cancellationToken);
                    }
                }

                break;
            }

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
                        var newExpDate = subscriptionReminder.Subscription.Expires;
                        if (newExpDate.AddDays(14) < DateTimeOffset.UtcNow)
                            // start a new subscription if it's too far in the past
                            newExpDate = DateTimeOffset.UtcNow.Date;

                        var sub = subscriptionReminder.Subscription;
                        if (sub.Product.DurationType == DurationTypes.Day)
                            sub.Expires = newExpDate.AddDays(sub.Product.Duration).ToUniversalTime();
                        else if (sub.Product.DurationType == DurationTypes.Month)
                            sub.Expires = newExpDate.AddMonths(sub.Product.Duration).ToUniversalTime();

                        sub.State = SubscriptionStates.Active;

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
