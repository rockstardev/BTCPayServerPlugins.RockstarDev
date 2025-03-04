using System.IO;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Abstractions.Services;
using BTCPayServer.HostedServices.Webhooks;
using BTCPayServer.RockstarDev.Plugins.Subscriptions.Data;
using BTCPayServer.RockstarDev.Plugins.Subscriptions.Services;
using BTCPayServer.Services.Apps;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.RockstarDev.Plugins.Subscriptions
{
    public class SubscriptionPlugin : BaseBTCPayServerPlugin
    {
        public const string SubscriptionPluginNavKey = "SubscriptionPluginNavKey";
        
        public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
        [
            new() {Identifier = nameof(BTCPayServer), Condition = ">=2.0.0"}
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
}