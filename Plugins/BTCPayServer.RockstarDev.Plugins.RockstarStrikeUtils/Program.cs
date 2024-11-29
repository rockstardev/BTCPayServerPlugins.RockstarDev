using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Data;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils
{
    public class RockstarStrikeUtilsPlugin : BaseBTCPayServerPlugin
    {
        public const string PluginNavKey = "RockstarStrikeUtilsNav";
        public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
        {
            new() {Identifier = nameof(BTCPayServer), Condition = ">=2.0.0"}
        };

        public override void Execute(IServiceCollection serviceCollection)
        {
            serviceCollection.AddUIExtension("store-integrations-nav", PluginNavKey);
            
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
}