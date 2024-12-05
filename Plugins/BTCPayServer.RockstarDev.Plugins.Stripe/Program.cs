using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.RockstarDev.Plugins.Stripe.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Strike.Client;
using Stripe;

namespace BTCPayServer.RockstarDev.Plugins.Stripe
{
    public class StripePlugin : BaseBTCPayServerPlugin
    {
        public const string PluginNavKey = "StripeNav";
        public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
        {
            new() {Identifier = nameof(BTCPayServer), Condition = ">=2.0.0"}
        };

        public override void Execute(IServiceCollection serviceCollection)
        {
            serviceCollection.AddUIExtension("store-integrations-nav", PluginNavKey);
            
            // stripe registrations
            StripeConfiguration.ApiKey =
                "xxx";
            serviceCollection.AddSingleton<StripeService>();
            
            // strike registrations
            serviceCollection.AddStrikeHttpClient();
            serviceCollection.AddStrikeClient();

            base.Execute(serviceCollection);
        }
    }
}