using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.RockstarDev.Plugins.Vouchers;

public class VoucherPlugin : BaseBTCPayServerPlugin
{
    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } = { new() { Identifier = nameof(BTCPayServer), Condition = ">=2.1.0" } };

    public override void Execute(IServiceCollection applicationBuilder)
    {
        applicationBuilder.AddUIExtension("store-integrations-nav", "VoucherNav");
        base.Execute(applicationBuilder);
    }
}
