using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.RockstarDev.Plugins.VendorPay.Data.Models;

// TODO: Length limits on strings in model, to enhance performance
public class PayrollUser
{
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public string Id { get; set; }

    public string Name { get; set; }

    public string Email { get; set; }
    public string Password { get; set; }
    public string EmailReminder { get; set; }
    public DateTime? LastReminderSent { get; set; }

    [MaxLength(50)]
    public string StoreId { get; set; }

    // Reserved for a future paired-output payout revision. NOT read by any
    // production code path today (only by StonewallDecoyRotationTests
    // exercising the SelectDecoyAddress helper in isolation). Column is
    // preserved so the future revision can populate + read it without a
    // second schema migration. Do not surface a form field, UI validator,
    // or payout hook against this column until the paired-output shape is
    // reworked to use sender-controlled change addresses (see
    // VendorPayStoreSetting.StonewallEnabled note for the full rationale).
    [MaxLength(1000)]
    public string StonewallDecoyAddresses { get; set; }

    public ICollection<PayrollInvoice> PayrollInvoices { get; set; } = new List<PayrollInvoice>();

    // TODO: Adding State property
    public VendorPayUserState State { get; set; }
    // Having a public page where user can visit the link and complete registration by entering password and activating account

    internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
    }
}
