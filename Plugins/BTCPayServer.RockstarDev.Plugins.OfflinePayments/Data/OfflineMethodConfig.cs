using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.RockstarDev.Plugins.OfflinePayments.Data;

public class OfflineMethodConfig
{
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public string Id { get; set; }
    public string StoreId { get; set; }
    public string MethodId { get; set; }
    public string DisplayName { get; set; }
    public string Instructions { get; set; } 
    public string BankName { get; set; }
    public string BankAddress { get; set; }
    public string AccountName { get; set; }
    public string AccountAddress { get; set; }
    public string RoutingNumber { get; set; }
    public string AccountNumber { get; set; }
    public string ReferenceTemplate { get; set; } = "Invoice {InvoiceId}";
    public string EstimatedSettlementTime { get; set; }
    public string SupportContact { get; set; }
    public bool RequiresManualConfirmation { get; set; } = true;
    public bool IsEnabled { get; set; } = true;
    public int SortOrder { get; set; } = 0;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public ICollection<OfflinePendingPayment> PendingPayments { get; set; } = new List<OfflinePendingPayment>();

    internal static void OnModelCreating(ModelBuilder builder)
    {
        builder
            .Entity<OfflineMethodConfig>()
            .HasIndex(x => new { x.StoreId, x.MethodId })
            .IsUnique();
    }
}
