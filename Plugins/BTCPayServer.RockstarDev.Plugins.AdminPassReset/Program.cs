using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.RockstarDev.Plugins.AdminPassReset;

public class AdminPassResetPlugin : BaseBTCPayServerPlugin
{
    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    [
        new() { Identifier = nameof(BTCPayServer), Condition = ">=1.12.0" }
    ];

    public override void Execute(IServiceCollection applicationBuilder)
    {
        applicationBuilder.AddUIExtension("store-integrations-nav", "PluginNav");
        base.Execute(applicationBuilder);
    }
}
