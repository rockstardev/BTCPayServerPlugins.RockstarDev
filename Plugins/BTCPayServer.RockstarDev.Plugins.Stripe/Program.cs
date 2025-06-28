using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.RockstarDev.Plugins.Stripe.Data;
using BTCPayServer.RockstarDev.Plugins.Stripe.Logic;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.RockstarDev.Plugins.Stripe;

public class StripePlugin : BaseBTCPayServerPlugin
{
    public const string PluginNavKey = "StripeNav";

    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    [
        new() { Identifier = nameof(BTCPayServer), Condition = ">=2.1.5" }
    ];

    public override void Execute(IServiceCollection serviceCollection)
    {
        serviceCollection.AddUIExtension("store-integrations-nav", PluginNavKey);

        // Add the database related registrations
        serviceCollection.AddSingleton<StripeDbContextFactory>();
        serviceCollection.AddDbContext<StripeDbContext>((provider, o) =>
        {
            var factory = provider.GetRequiredService<StripeDbContextFactory>();
            factory.ConfigureBuilder(o);
        });

        // this initializes the stripe api key from database as well
        serviceCollection.AddHostedService<StripeMigrationRunner>();

        serviceCollection.AddSingleton<StripeClientFactory>();

        base.Execute(serviceCollection);
    }
}
