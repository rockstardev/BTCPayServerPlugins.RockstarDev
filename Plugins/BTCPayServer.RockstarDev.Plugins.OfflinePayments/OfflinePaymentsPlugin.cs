using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Payments;
using BTCPayServer.RockstarDev.Plugins.OfflinePayments.Data;
using BTCPayServer.RockstarDev.Plugins.OfflinePayments.PaymentHandlers;
using BTCPayServer.RockstarDev.Plugins.OfflinePayments.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.RockstarDev.Plugins.OfflinePayments;

public class OfflinePaymentsPlugin : BaseBTCPayServerPlugin
{
    public const string PluginNavKey = nameof(OfflinePaymentsPlugin) + "Nav";
    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    [
        new() { Identifier = nameof(BTCPayServer), Condition = ">=2.3.6" }
    ];

    public override void Execute(IServiceCollection services)
    {
        services.AddUIExtension("store-wallets-nav", "OfflinePaymentsStoreNav");
        services.AddUIExtension("store-integrations-nav", "OfflinePaymentNav");
        services.AddUIExtension("checkout-payment", "OfflinePaymentsCheckout");
        services.AddSingleton<OfflinePaymentsService>();
        services.AddSingleton<OfflineMethodConfigService>();
        services.AddMemoryCache();

        services.AddSingleton<OfflinePaymentPluginDbContextFactory>();
        services.AddDbContext<OfflinePaymentPluginDbContext>((provider, o) =>
        {
            var factory = provider.GetRequiredService<OfflinePaymentPluginDbContextFactory>();
            factory.ConfigureBuilder(o);
        });
        services.AddHostedService<PluginMigrationRunner>();

        var knownMethods = new[] { "ACH", "WIRE", "CHECK" };
        foreach (var method in knownMethods)
        {
            var pmid = new PaymentMethodId(method);
            services.AddSingleton<IPaymentMethodHandler>(provider => ActivatorUtilities.CreateInstance<OfflinePaymentMethodHandler>(provider, pmid));
            services.AddSingleton<ICheckoutModelExtension>(provider => ActivatorUtilities.CreateInstance<OfflineCheckoutModelExtension>(provider, pmid));
            services.AddDefaultPrettyName(pmid, method);
        }

        base.Execute(services);
    }
}
