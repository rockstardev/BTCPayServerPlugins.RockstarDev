using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Data.Models;

public class PayrollInvoice
{
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public string Id { get; set; }

    [DisplayName("User ID")]
    public string UserId { get; set; }
    public PayrollUser User { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Destination { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; }
    public string Description { get; set; }
    public string InvoiceFilename { get; set; }
    public bool IsArchived { get; set; }
    public PayrollInvoiceState State { get; set; }

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
    InProgress, // waiting for confirmation on blockchain (or for lightning it can be stuck HTLC
    Completed,
    Cancelled
}