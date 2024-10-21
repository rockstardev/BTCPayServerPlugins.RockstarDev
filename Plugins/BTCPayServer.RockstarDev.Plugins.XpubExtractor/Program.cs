using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.RockstarDev.Plugins.XpubExtractor
{
    public class Program : BaseBTCPayServerPlugin
    {
        public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
        {
            new() {Identifier = nameof(BTCPayServer), Condition = ">=1.12.0"}
        };

        public override void Execute(IServiceCollection applicationBuilder)
        {
            // backwards compatibile way to add UI extension, so it works on older versions of BTCPayServer
            applicationBuilder.AddSingleton<IUIExtension>(new UIExtension("XpubExtractorNav",
                "store-integrations-nav"));
            
            base.Execute(applicationBuilder);
        }
    }
}