using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Services.Apps;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BTCPayServer.RockstarDev.Plugins.Vouchers;

public class VoucherPlugin : BaseBTCPayServerPlugin
{
    public const string SettingsName = "VoucherSettings";
    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    [
        new() { Identifier = nameof(BTCPayServer), Condition = ">=2.3.0" }
    ];

    public override void Execute(IServiceCollection applicationBuilder)
    {
        applicationBuilder.AddSingleton<AppBaseType, VoucherPluginAppType>();
        applicationBuilder.AddUIExtension("store-integrations-nav", "VoucherNav");
        applicationBuilder.AddUIExtension("pos-header", "PosViewSwitcher");
        base.Execute(applicationBuilder);
    }
}

public class VoucherPluginAppType : AppBaseType
{
    public const string AppType = "VoucherPlugin";
    private readonly LinkGenerator _linkGenerator;
    private readonly BTCPayServerOptions _btcPayServerOptions;

    public VoucherPluginAppType(LinkGenerator linkGenerator, IOptions<BTCPayServerOptions> btcPayServerOptions) : base(AppType)
    {
        _linkGenerator = linkGenerator;
        _btcPayServerOptions = btcPayServerOptions.Value;
    }

    public class AppConfig : PointOfSaleSettings
    {
    }

    public override Task<object?> GetInfo(AppData appData)
        => Task.FromResult<object?>(null);

    public override Task<string> ConfigureLink(AppData app)
    {
        return Task.FromResult(_linkGenerator.GetPathByAction(nameof(VoucherController.ListVouchers), "Voucher", new { storeId = app.StoreDataId },
            _btcPayServerOptions.RootPath)!);
    }

    public override Task<string> ViewLink(AppData app)
    {
        return Task.FromResult(_linkGenerator.GetPathByAction(nameof(VoucherController.ListVouchers), "Voucher", new { storeId = app.StoreDataId },
            _btcPayServerOptions.RootPath)!);
    }

    public override Task SetDefaultSettings(AppData appData, string defaultCurrency)
    {
        throw new System.NotImplementedException();
    }
}
