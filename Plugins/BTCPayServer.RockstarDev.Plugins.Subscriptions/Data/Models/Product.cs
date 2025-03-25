﻿using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.RockstarDev.Plugins.Subscriptions.Data.Models;

public class Product
{
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public string Id { get; set; }

    [StringLength(100)] public string Name { get; set; }

    public decimal Price { get; set; }

    [StringLength(4)] public string Currency { get; set; }

    public int Duration { get; set; }

    public DurationTypes DurationType { get; set; }

    [StringLength(25)] public string? ReminderDays { get; set; }

    [MaxLength(50)] public string StoreId { get; set; }

    [MaxLength(50)] public string? FormId { get; set; }

    public ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();

    internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
    }
}

public enum DurationTypes
{
    Day,
    Month
}