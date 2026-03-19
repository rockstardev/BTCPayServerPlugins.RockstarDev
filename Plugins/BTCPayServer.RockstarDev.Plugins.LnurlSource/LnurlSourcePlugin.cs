#nullable enable
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Abstractions.Services;
using BTCPayServer.Lightning;
using BTCPayServer.RockstarDev.Plugins.LnurlSource.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.RockstarDev.Plugins.LnurlSource;

public class LnurlSourcePlugin : BaseBTCPayServerPlugin
{
    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    {
        new() { Identifier = nameof(BTCPayServer), Condition = ">=2.0.0" }
    };

    public override void Execute(IServiceCollection applicationBuilder)
    {
        applicationBuilder.AddUIExtension("ln-payment-method-setup-tab",
            "LnurlSource/LNPaymentMethodSetupTab");
        applicationBuilder.AddSingleton<ILightningConnectionStringHandler>(provider =>
            provider.GetRequiredService<LnurlSourceConnectionStringHandler>());
        applicationBuilder.AddSingleton<LnurlSourceConnectionStringHandler>();

        applicationBuilder.AddSingleton<LnurlSourceDbContextFactory>();
        applicationBuilder.AddDbContext<LnurlSourceDbContext>((provider, o) =>
        {
            var factory = provider.GetRequiredService<LnurlSourceDbContextFactory>();
            factory.ConfigureBuilder(o);
        });
        applicationBuilder.AddHostedService<LnurlSourceMigrationRunner>();

        base.Execute(applicationBuilder);
    }
}
