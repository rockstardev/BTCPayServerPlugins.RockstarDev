using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.RockstarDev.Plugins.Subscriptions.Data.Models;

public class Customer
{
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public string Id { get; set; }

    [StringLength(100)] public string Name { get; set; }

    [StringLength(100)] public string Email { get; set; }

    [StringLength(100)] public string Address1 { get; set; }

    [StringLength(50)] public string Address2 { get; set; }

    [StringLength(85)] public string City { get; set; }

    [StringLength(56)] public string Country { get; set; }

    [StringLength(20)] public string ZipCode { get; set; }

    [MaxLength(50)] public string StoreId { get; set; }

    public ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();

    internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
    }


    // TODO: Define entity
}