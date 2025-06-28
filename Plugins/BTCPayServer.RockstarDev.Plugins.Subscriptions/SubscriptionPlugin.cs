using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.RockstarDev.Plugins.Subscriptions.Data;
using BTCPayServer.RockstarDev.Plugins.Subscriptions.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.RockstarDev.Plugins.Subscriptions;

public class SubscriptionPlugin : BaseBTCPayServerPlugin
{
    public const string SubscriptionPluginNavKey = "SubscriptionsNav";

    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    [
        new() { Identifier = nameof(BTCPayServer), Condition = ">=2.1.5" }
    ];

    public override void Execute(IServiceCollection serviceCollection)
    {
        serviceCollection.AddUIExtension("store-integrations-nav", SubscriptionPluginNavKey);

        serviceCollection.AddSingleton<SubscriptionService>();
        serviceCollection.AddHostedService(s => s.GetRequiredService<SubscriptionService>());
        serviceCollection.AddSingleton<EmailService>();

        // Add the database related registrations
        serviceCollection.AddSingleton<PluginDbContextFactory>();
        serviceCollection.AddDbContext<PluginDbContext>((provider, o) =>
        {
            var factory = provider.GetRequiredService<PluginDbContextFactory>();
            factory.ConfigureBuilder(o);
        });
        serviceCollection.AddHostedService<PluginMigrationRunner>();

        base.Execute(serviceCollection);
    }
}
