using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Data;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Security;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Services;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Services.Helpers;
using BTCPayServer.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace BTCPayServer.RockstarDev.Plugins.VendorPay;

// ReSharper disable once UnusedType.Global
public class VendorPayPlugin : BaseBTCPayServerPlugin
{
    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    [
        new() { Identifier = nameof(BTCPayServer), Condition = ">=2.3.0" }
    ];

    public override void Execute(IServiceCollection serviceCollection)
    {
        serviceCollection.AddUIExtension("store-integrations-nav", "VendorPayNav");

        // Register authorization handler and policy for custom permission
        serviceCollection.AddScoped<IAuthorizationHandler, VendorPayAuthorizationHandler>();
        serviceCollection.AddAuthorization(options =>
        {
            options.AddPolicy(VendorPayPermissions.CanManageVendorPay,
                policy => policy.AddRequirements(new PolicyRequirement(VendorPayPermissions.CanManageVendorPay)));
        });

        // Inject plugin permissions directly into DI container
        // This follows the maintainer's suggestion to avoid interfaces
        // foreach (var permission in VendorPayPermissions.AllPermissions(Identifier))
        // {
        //     serviceCollection.AddSingleton(permission);
        // }


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
