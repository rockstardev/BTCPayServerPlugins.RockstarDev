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
    public string ResolvedReference { get; set; }
    public OfflinePaymentStatus Status { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string CustomerNote { get; set; }
    public DateTimeOffset? CustomerMarkedSentAt { get; set; }
    public DateTimeOffset? AdminConfirmedAt { get; set; }
    public DateTimeOffset? AdminInvalidatedAt { get; set; }
    public string RemittanceFileUrl { get; set; }
    public string AdminUserId { get; set; }
    public string MethodConfigId { get; set; }

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

        builder
            .Entity<OfflinePendingPayment>()
            .HasIndex(x => new { x.StoreId, x.InvoiceId })
            .IsUnique();
    }
}

public enum OfflinePaymentStatus
{
    CustomerMarkedSent = 1, AdminConfirmed = 2, AdminInvalidated = 3
}
