#nullable enable
using System;
using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.RockstarDev.Plugins.LnurlVerify.Data.Models;

public class LnurlVerifyInvoice
{
    [Key]
    [MaxLength(64)]
    public string PaymentHash { get; set; } = string.Empty;

    [MaxLength(20)]
    public string InvoiceId { get; set; } = string.Empty;

    [MaxLength(500)]
    public string VerifyUrl { get; set; } = string.Empty;

    public string Bolt11 { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }

    public long AmountMilliSatoshi { get; set; }
}
