using System;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data;
using BTCPayServer.RockstarDev.Plugins.Payroll.Services;
using BTCPayServer.RockstarDev.Plugins.Payroll.Services.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BTCPayServer.RockstarDev.Plugins.Payroll;

// ReSharper disable once UnusedType.Global
public class PayrollPlugin : BaseBTCPayServerPlugin
{
    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    [
        new() { Identifier = nameof(BTCPayServer), Condition = ">=2.2.0" }
    ];

    public override void Execute(IServiceCollection serviceCollection)
    {
        serviceCollection.AddUIExtension("store-integrations-nav", "PayrollNav");

        serviceCollection.AddSingleton<VendorPayPassHasher>();
        serviceCollection.AddSingleton<EmailService>();

        // hosted services
        serviceCollection.AddSingleton<IHostedService, VendorPayPaidHostedService>();
        serviceCollection.AddSingleton<IHostedService>(provider =>
            provider.GetService<VendorPayEmailReminderService>());
        serviceCollection.AddScheduledTask<VendorPayEmailReminderService>(TimeSpan.FromHours(12));

        // helpers
        serviceCollection.AddTransient<PayrollInvoiceUploadHelper>();
        serviceCollection.AddTransient<InvoicesDownloadHelper>();

        // Add the database related registrations
        serviceCollection.AddSingleton<PluginDbContextFactory>();
        serviceCollection.AddDbContext<PluginDbContext>((provider, o) =>
        {
            var factory = provider.GetRequiredService<PluginDbContextFactory>();
            factory.ConfigureBuilder(o);
        });
        serviceCollection.AddHostedService<PluginMigrationRunner>();
        serviceCollection.AddReportProvider<VendorPayReportProvider>();

        base.Execute(serviceCollection);
    }
}
