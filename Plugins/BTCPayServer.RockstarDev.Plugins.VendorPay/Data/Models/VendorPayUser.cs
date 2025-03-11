using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BTCPayServer.RockstarDev.Plugins.VendorPay.Data.Models;

// TODO: Length limits on strings in model, to enhance performance
public class VendorPayUser
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
    public ICollection<VendorPayInvoice> PayrollInvoices { get; set; } = new List<VendorPayInvoice>();

    // TODO: Adding State property
    public VendorPayUserState State { get; set; }
    // Having a public page where user can visit the link and complete registration by entering password and activating account

    internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
    }
}
