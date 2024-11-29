using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
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

        public override void Execute(IServiceCollection applicationBuilder)
        {
            applicationBuilder.AddUIExtension("store-integrations-nav", RockstarStrikeUtilsPlugin.PluginNavKey);
            base.Execute(applicationBuilder);
        }
    }
}