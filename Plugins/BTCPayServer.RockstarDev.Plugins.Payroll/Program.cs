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
        new() {Identifier = nameof(BTCPayServer), Condition = ">=1.12.4"}
    };

    public override void Execute(IServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton<IUIExtension>(new UIExtension("PayrollNav",
            "store-integrations-nav"));

        serviceCollection.AddSingleton<PayrollPluginPassHasher>();
        serviceCollection.AddSingleton<PayrollPluginDbContextFactory>();
        serviceCollection.AddDbContext<PayrollPluginDbContext>((provider, o) =>
        {
            var factory = provider.GetRequiredService<PayrollPluginDbContextFactory>();
            factory.ConfigureBuilder(o);
        });
        serviceCollection.AddHostedService<PayrollPluginMigrationRunner>();

        base.Execute(serviceCollection);
    }
}