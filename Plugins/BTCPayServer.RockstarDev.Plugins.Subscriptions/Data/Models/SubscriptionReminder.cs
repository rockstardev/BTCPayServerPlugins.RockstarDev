using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BTCPayServer.RockstarDev.Plugins.Subscriptions.Data.Models;

public class SubscriptionReminder
{
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public string Id { get; set; }

    [MaxLength(36)] // guid
    public string SubscriptionId { get; set; }
    public Subscription Subscription { get; set; }

    public DateTimeOffset Created { get; set; }
    
    internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .Entity<SubscriptionReminder>()
            .HasOne(o => o.Subscription)
            .WithMany(w => w.SubscriptionReminders)
            .OnDelete(DeleteBehavior.Cascade);
    }
}