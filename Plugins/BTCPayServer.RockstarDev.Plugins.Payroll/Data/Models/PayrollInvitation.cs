using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Data.Models;

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

    public bool IsExpired => CreatedAt.AddDays(7) < DateTime.UtcNow;

    internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
    }
}
