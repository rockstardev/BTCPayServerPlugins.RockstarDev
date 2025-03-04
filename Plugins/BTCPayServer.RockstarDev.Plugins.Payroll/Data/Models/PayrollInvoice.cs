using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Data.Models;

// TODO: Length limits on strings in model, to enhance performance
public class PayrollInvoice
{
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public string Id { get; set; }

    [DisplayName("User ID")]
    [MaxLength(36)] // guid
    public string UserId { get; set; }
    public PayrollUser User { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(100)]
    public string Destination { get; set; }
    public decimal Amount { get; set; }
    [MaxLength(5)]
    public string Currency { get; set; }
    [MaxLength(20)]
    public string PurchaseOrder { get; set; }
    [MaxLength(300)]
    public string Description { get; set; }
    [MaxLength(36)] // guid
    public string InvoiceFilename { get; set; }
    public string ExtraFilenames { get; set; }
    public bool IsArchived { get; set; }
    public PayrollInvoiceState State { get; set; }
    [MaxLength(100)] 
    public string TxnId { get; set; }
    [MaxLength(20)] 
    public string BtcPaid { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    [MaxLength(500)] 
    public string AdminNote { get; set; }

    internal static void OnModelCreating(ModelBuilder builder)
    {
        builder
            .Entity<PayrollInvoice>()
            .HasOne(o => o.User)
            .WithMany(w => w.PayrollInvoices)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public enum PayrollInvoiceState
{
    AwaitingApproval,
    AwaitingPayment,
    InProgress, // waiting for confirmation on blockchain (or for lightning it can be stuck HTLC)
    Completed,
    Cancelled
}