using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.RockstarDev.Plugins.WalletHistoryReload.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.RockstarDev.Plugins.WalletHistoryReload;

public class WalletHistoryReloadPlugin : BaseBTCPayServerPlugin
{
    public const string PluginNavKey = nameof(WalletHistoryReloadPlugin) + "Nav";

    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    [
        new() { Identifier = nameof(BTCPayServer), Condition = ">=2.1.5" }
    ];

    public override void Execute(IServiceCollection serviceCollection)
    {
        serviceCollection.AddUIExtension("store-wallets-nav", PluginNavKey);
        
        // Register services
        serviceCollection.AddHttpClient(); // Required for API calls
        serviceCollection.AddSingleton<MempoolSpaceApiService>();
        serviceCollection.AddSingleton<HistoricalPriceService>();
        serviceCollection.AddSingleton<NBXplorerDbService>();
        serviceCollection.AddSingleton<TransactionDataBackfillService>();
        
        base.Execute(serviceCollection);
    }
}
