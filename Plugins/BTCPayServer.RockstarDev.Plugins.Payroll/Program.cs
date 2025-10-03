using System;
using System.Reflection;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data;
using BTCPayServer.RockstarDev.Plugins.Payroll.Services;
using BTCPayServer.RockstarDev.Plugins.Payroll.Services.Helpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
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


        var assembly = Assembly.GetExecutingAssembly();
        serviceCollection.PostConfigure<StaticFileOptions>(options =>
        {
            var pluginProvider = new ManifestEmbeddedFileProvider(assembly, "Resources");

            options.FileProvider = new CompositeFileProvider(
                options.FileProvider ?? new NullFileProvider(),
                pluginProvider
            );
        });
        base.Execute(serviceCollection);
    }
}
