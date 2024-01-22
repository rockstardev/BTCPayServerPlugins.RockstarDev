using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Abstractions.Services;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.RockstarDev.Plugins.Payroll;

public class PayrollPlugin : BaseBTCPayServerPlugin
{
    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    {
        new() {Identifier = nameof(BTCPayServer), Condition = ">=1.12.0"}
    };

    public override void Execute(IServiceCollection applicationBuilder)
    {
        applicationBuilder.AddSingleton<IUIExtension>(new UIExtension("PayrollNav",
            "store-integrations-nav"));

        applicationBuilder.AddSingleton<PayrollPluginDbContextFactory>();
        applicationBuilder.AddDbContext<PayrollPluginDbContext>((provider, o) =>
        {
            var factory = provider.GetRequiredService<PayrollPluginDbContextFactory>();
            factory.ConfigureBuilder(o);
        });

        base.Execute(applicationBuilder);
    }
}