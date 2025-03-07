using System;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Data;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Services;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Services.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BTCPayServer.RockstarDev.Plugins.VendorPay;

// ReSharper disable once UnusedType.Global
public class VendorPayPlugin : BaseBTCPayServerPlugin
{
    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    {
        new() {Identifier = nameof(BTCPayServer), Condition = ">=2.0.0"}
    };

    public override void Execute(IServiceCollection serviceCollection)
    {
        serviceCollection.AddUIExtension("store-integrations-nav", "VendorPayNav");

        serviceCollection.AddSingleton<VendorPayPassHasher>();
        serviceCollection.AddSingleton<EmailService>();
        
        // hosted services
        serviceCollection.AddSingleton<IHostedService, VendorPayPaidHostedService>();
        serviceCollection.AddSingleton<IHostedService>(provider =>
            provider.GetService<VendorPayEmailReminderService>());
        serviceCollection.AddScheduledTask<VendorPayEmailReminderService>(TimeSpan.FromHours(12));

        // helpers
        serviceCollection.AddTransient<VendorPayInvoiceUploadHelper>();
        serviceCollection.AddTransient<InvoicesDownloadHelper>();

        // Add the database related registrations
        serviceCollection.AddSingleton<VendorPayPluginDbContextFactory>();
        serviceCollection.AddDbContext<VendorPayPluginDbContext>((provider, o) =>
        {
            var factory = provider.GetRequiredService<VendorPayPluginDbContextFactory>();
            factory.ConfigureBuilder(o);
        });
        serviceCollection.AddHostedService<VendorPayPluginMigrationRunner>();

        base.Execute(serviceCollection);
    }
}