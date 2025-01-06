using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.RockstarDev.Plugins.BitcoinStacker;

// ReSharper disable once ClassNeverInstantiated.Global
public class BitcoinStackerPlugin : BaseBTCPayServerPlugin
{
    public const string PluginNavKey = nameof(BitcoinStackerPlugin) + "Nav";
    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    [
        new() {Identifier = nameof(BTCPayServer), Condition = ">=2.0.0"}
    ];

    public override void Execute(IServiceCollection applicationBuilder)
    {
        applicationBuilder.AddUIExtension("store-integrations-nav", "PluginNav");
        base.Execute(applicationBuilder);
    }
}