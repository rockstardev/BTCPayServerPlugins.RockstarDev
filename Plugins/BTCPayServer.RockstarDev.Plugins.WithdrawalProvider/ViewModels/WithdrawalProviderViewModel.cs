using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.RockstarDev.Plugins.WithdrawalProvider.Services;

namespace BTCPayServer.RockstarDev.Plugins.WithdrawalProvider.ViewModels;

public class WithdrawalProviderViewModel
{
    public string StoreId { get; set; } = string.Empty;

    [Display(Name = "Enabled")]
    public bool Enabled { get; set; }

    [Display(Name = "API Key")]
    public string ApiKey { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Ticker")]
    public string Ticker { get; set; } = "BTCEUR";

    [Required]
    [Display(Name = "Fiat Currency")]
    public string FiatCurrency { get; set; } = "EUR";

    [Required]
    [Display(Name = "Default Payment Method")]
    public string PaymentMethod { get; set; } = "LIGHTNING";

    [Range(1, long.MaxValue)]
    [Display(Name = "Source Amount (sats)")]
    public long SourceAmountSats { get; set; } = 100_000;

    [Required]
    [Display(Name = "IP Address")]
    public string IpAddress { get; set; } = "127.0.0.1";

    public string? LastOrderId { get; set; }
    public string? LastOrderDestination { get; set; }

    public string? UserId { get; set; }
    public decimal? Balance { get; set; }
    public WithdrawalProviderClient.RateResponse? Rate { get; set; }
    public IReadOnlyList<WithdrawalProviderClient.ProviderTransaction> Transactions { get; set; } = [];
}
