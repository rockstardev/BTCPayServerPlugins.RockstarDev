using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.RockstarDev.Plugins.Subscriptions.Data.Models;

public class SubscriptionReminder
{
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public string Id { get; set; }

    [MaxLength(36)] // guid
    public required string SubscriptionId { get; set; }

    public Subscription Subscription { get; set; }

    public DateTimeOffset Created { get; set; }

    public DateTimeOffset? ClickedAt { get; set; }

    [MaxLength(36)] public string? PaymentRequestId { get; set; }

    public string? DebugAdditionalData { get; set; }

    internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .Entity<SubscriptionReminder>()
            .HasOne(o => o.Subscription)
            .WithMany(w => w.SubscriptionReminders)
            .OnDelete(DeleteBehavior.Cascade);
    }
}