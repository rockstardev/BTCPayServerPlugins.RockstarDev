using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.RockstarDev.Plugins.XpubExtractor
{
    public class XpubExtractorPlugin : BaseBTCPayServerPlugin
    {
        public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
        {
            new() {Identifier = nameof(BTCPayServer), Condition = ">=2.0.0"}
        };

        public override void Execute(IServiceCollection serviceCollection)
        {
            serviceCollection.AddUIExtension("store-integrations-nav", "XpubExtractorNav");
            base.Execute(serviceCollection);
        }
    }
}