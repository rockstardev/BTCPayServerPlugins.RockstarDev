using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.RockstarDev.Plugins.Stripe.ViewModels.Stripe;

public class ConfigurationViewModel
{
    [Required]
    public string StripeApiKey { get; set; }
}