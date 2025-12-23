using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.RockstarDev.Plugins.VendorPay.Data.Models;

public class PayrollInvitation
{
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public string Id { get; set; }

    [Required]
    [EmailAddress]
    public string Email { get; set; }

    public string Token { get; set; }
    public string StoreId { get; set; }
    public string Name { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? AcceptedAt { get; set; }

    internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
    }
}
