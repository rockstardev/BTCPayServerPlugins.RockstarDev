using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.RockstarDev.Plugins.TransactionCounter.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.RockstarDev.Plugins.TransactionCounter;

public class CounterPlugin : BaseBTCPayServerPlugin
{
    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    [
        new() { Identifier = nameof(BTCPayServer), Condition = ">=2.1.0" }
    ];

    public override void Execute(IServiceCollection services)
    {
        services.AddUIExtension("server-nav", "PluginCounterNav");
        services.AddSingleton<TransactionCounter>();
        base.Execute(services);
    }
}
