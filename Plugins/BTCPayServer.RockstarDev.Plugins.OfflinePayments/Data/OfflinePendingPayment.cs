using System;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.RockstarDev.Plugins.OfflinePayments.Data;

public class OfflinePendingPayment
{
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public string Id { get; set; }
    public string StoreId { get; set; }
    public string InvoiceId { get; set; }
    public string MethodId { get; set; }
    public string ResolvedReference { get; set; } // rendered from template e.g. "Invoice INV-123"
    public OfflinePaymentStatus Status { get; set; } = OfflinePaymentStatus.MethodSelected;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? InstructionsViewedAt { get; set; }
    public DateTimeOffset? CustomerMarkedSentAt { get; set; }
    public DateTimeOffset? AdminConfirmedAt { get; set; }
    public DateTimeOffset? AdminInvalidatedAt { get; set; }
    public string CustomerNote { get; set; }
    public string RemittanceFileUrl { get; set; }
    public string AdminUserId { get; set; }
    public string AdminNote { get; set; }
    public string MethodConfigId { get; set; }
    // Navigation
    [ForeignKey(nameof(MethodConfigId))]
    public OfflineMethodConfig? MethodConfig { get; set; }

    internal static void OnModelCreating(ModelBuilder builder)
    {
        builder
            .Entity<OfflinePendingPayment>()
            .HasOne(o => o.MethodConfig)
            .WithMany(w => w.PendingPayments)
            .HasForeignKey(f => f.MethodConfigId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public enum OfflinePaymentStatus
{
    MethodSelected = 0,         // customer picked the method
    InstructionsViewed = 1,     // customer saw instructions page
    CustomerMarkedSent = 2,     // customer clicked "I've sent payment"
    AdminConfirmed = 3,         // admin confirmed funds received -> settles invoice
    AdminInvalidated = 4        // admin rejected / voided
}
