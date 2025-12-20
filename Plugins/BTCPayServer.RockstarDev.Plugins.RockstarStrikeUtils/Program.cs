using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Data;
using BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Logic;
using Microsoft.Extensions.DependencyInjection;
using Strike.Client;

namespace BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils;

// ReSharper disable once ClassNeverInstantiated.Global
public class RockstarStrikeUtilsPlugin : BaseBTCPayServerPlugin
{
    public const string PluginStrikeNavKey = "RockstarStrikeUtilsNav";

    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    [
        new() { Identifier = nameof(BTCPayServer), Condition = ">=2.1.5" }
    ];

    public override void Execute(IServiceCollection serviceCollection)
    {
        serviceCollection.AddUIExtension("store-integrations-nav", PluginStrikeNavKey);

        // strike registrations
        serviceCollection.AddStrikeHttpClient();
        serviceCollection.AddStrikeClient();

        serviceCollection.AddScoped<StrikeClientFactory>();

        // Add the database related registrations
        serviceCollection.AddSingleton<RockstarStrikeDbContextFactory>();
        serviceCollection.AddDbContext<RockstarStrikeDbContext>((provider, o) =>
        {
            var factory = provider.GetRequiredService<RockstarStrikeDbContextFactory>();
            factory.ConfigureBuilder(o);
        });
        serviceCollection.AddHostedService<RockstarStrikeMigrationRunner>();

        base.Execute(serviceCollection);
    }
}
