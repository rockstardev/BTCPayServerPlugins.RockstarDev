using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.RockstarDev.Plugins.OfflinePayments.Data;
using BTCPayServer.RockstarDev.Plugins.OfflinePayments.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.RockstarDev.Plugins.OfflinePayments;

public class OfflinePaymentsPlugin : BaseBTCPayServerPlugin
{
    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    [
        new() { Identifier = nameof(BTCPayServer), Condition = ">=2.3.6" }
    ];

    public override void Execute(IServiceCollection services)
    {
        services.AddUIExtension("store-integrations-nav", "OfflinePaymentNav");
        services.AddSingleton<OfflinePaymentsService>();
        services.AddSingleton<OfflineMethodConfigService>();

        services.AddSingleton<OfflinePaymentPluginDbContextFactory>();
        services.AddDbContext<OfflinePaymentPluginDbContext>((provider, o) =>
        {
            var factory = provider.GetRequiredService<OfflinePaymentPluginDbContextFactory>();
            factory.ConfigureBuilder(o);
        });

        services.AddHostedService<PluginMigrationRunner>();
        base.Execute(services);
    }
}
