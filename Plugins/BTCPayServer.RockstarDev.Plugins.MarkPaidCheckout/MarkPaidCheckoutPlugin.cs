using System;
using System.Linq;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Hosting;
using BTCPayServer.Payments;
using BTCPayServer.RockstarDev.Plugins.MarkPaidCheckout.PaymentHandlers;
using BTCPayServer.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.RockstarDev.Plugins.MarkPaidCheckout;

public class MarkPaidCheckoutPlugin : BaseBTCPayServerPlugin
{
    public const string PluginNavKey = nameof(MarkPaidCheckoutPlugin) + "Nav";
    public const string SettingKey = "RockstarDev.MarkPaidCheckout";
    public const string EnvVar = "MARKPAID_METHODS";

    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    [
        new() { Identifier = nameof(BTCPayServer), Condition = ">=2.1.5" }
    ];

    public override void Execute(IServiceCollection services)
    {
        // Determine methods list at startup. Priority: env var -> saved settings -> default "CASH"
        string? csv = Environment.GetEnvironmentVariable(EnvVar);
        try
        {
            if (string.IsNullOrWhiteSpace(csv))
            {
                using var sp = services.BuildServiceProvider();
                var settings = sp.GetService<BTCPayServer.Services.SettingsRepository>();
                var saved = settings?.GetSettingAsync<Server.MarkPaidSettings>(SettingKey).GetAwaiter().GetResult();
                csv ??= saved?.MethodsCsv;
            }
        }
        catch
        {
            // ignore; fallback to default
        }

        csv ??= "CASH";
        var methods = csv.Split(',', StringSplitOptions.RemoveEmptyEntries)
                         .Select(s => s.Trim())
                         .Where(s => !string.IsNullOrWhiteSpace(s))
                         .Select(s => s.ToUpperInvariant())
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .ToArray();
        var registry = new MarkPaidMethodsRegistry(methods);
        services.AddSingleton(registry);

        foreach (var method in registry.Methods)
        {
            var pmid = new PaymentMethodId(method);
            services.AddTransactionLinkProvider(pmid, new DefaultTransactionLinkProvider(null));
            services.AddSingleton<IPaymentMethodHandler>(provider => ActivatorUtilities.CreateInstance<MarkPaidPaymentMethodHandler>(provider, pmid));
            services.AddSingleton<ICheckoutModelExtension>(provider => ActivatorUtilities.CreateInstance<MarkPaidCheckoutModelExtension>(provider, pmid));
            services.AddDefaultPrettyName(pmid, method);
        }

        services.AddSingleton<MarkPaidStatusProvider>();

        // UI extensions
        services.AddUIExtension("checkout-payment", "MarkPaidLikeMethodCheckout");
        services.AddUIExtension("store-wallets-nav", "MarkPaidStoreNav");
        services.AddUIExtension("header-nav", "MarkPaid/NavExtension");

        base.Execute(services);
    }
}
