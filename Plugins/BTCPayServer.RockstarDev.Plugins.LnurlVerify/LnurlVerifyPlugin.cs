#nullable enable
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Abstractions.Services;
using BTCPayServer.Lightning;
using BTCPayServer.RockstarDev.Plugins.LnurlVerify.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.RockstarDev.Plugins.LnurlVerify;

public class LnurlVerifyPlugin : BaseBTCPayServerPlugin
{
    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    {
        new() { Identifier = nameof(BTCPayServer), Condition = ">=2.0.0" }
    };

    public override void Execute(IServiceCollection applicationBuilder)
    {
        applicationBuilder.AddUIExtension("ln-payment-method-setup-tab",
            "LnurlVerify/LNPaymentMethodSetupTab");
        applicationBuilder.AddSingleton<ILightningConnectionStringHandler>(provider =>
            provider.GetRequiredService<LnurlVerifyConnectionStringHandler>());
        applicationBuilder.AddSingleton<LnurlVerifyConnectionStringHandler>();

        applicationBuilder.AddSingleton<LnurlVerifyDbContextFactory>();
        applicationBuilder.AddDbContext<LnurlVerifyDbContext>((provider, o) =>
        {
            var factory = provider.GetRequiredService<LnurlVerifyDbContextFactory>();
            factory.ConfigureBuilder(o);
        });
        applicationBuilder.AddHostedService<LnurlVerifyMigrationRunner>();

        base.Execute(applicationBuilder);
    }
}
