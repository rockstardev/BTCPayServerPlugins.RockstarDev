using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BTCPayServer.RockstarDev.Plugins.Subscriptions.Data.Models;

public class Subscription
{
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public string Id { get; set; }

    [DisplayName("Customer ID")]
    [MaxLength(36)] // guid
    public string CustomerId { get; set; }
    public Customer Customer { get; set; }
    
    
    [DisplayName("Product ID")]
    [MaxLength(36)] // guid
    public string ProductId { get; set; }
    public Product Product { get; set; }

    public DateTimeOffset Created { get; set; }
    public DateTimeOffset Expires { get; set; }

    // for shopify and other systems
    [MaxLength(50)]
    public string ExternalId { get; set; }

    [MaxLength(10)]
    public SubscriptionStates State { get; set; }
    
    // for shopify and other systems
    [MaxLength(50)]
    public string PaymentRequestId { get; set; }
    
    // TODO: Define entity
    
    public ICollection<SubscriptionReminder> SubscriptionReminders { get; set; } = new List<SubscriptionReminder>();
    
    internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .Entity<Subscription>()
            .HasOne(o => o.Customer)
            .WithMany(w => w.Subscriptions)
            .OnDelete(DeleteBehavior.Cascade);
        
        modelBuilder
            .Entity<Subscription>()
            .HasOne(o => o.Product)
            .WithMany(w => w.Subscriptions)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public enum SubscriptionStates
{
    Disabled,
    Active
}