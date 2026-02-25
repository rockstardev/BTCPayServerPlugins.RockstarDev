using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.RockstarDev.Plugins.WithdrawalProvider.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.RockstarDev.Plugins.WithdrawalProvider;

public class WithdrawalProviderPlugin : BaseBTCPayServerPlugin
{
    public const string PluginNavKey = "WithdrawalProviderNav";

    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    [
        new() { Identifier = nameof(BTCPayServer), Condition = ">=2.1.5" }
    ];

    public override void Execute(IServiceCollection services)
    {
        services.AddUIExtension("store-integrations-nav", PluginNavKey);
        services.AddHttpClient(WithdrawalProviderClient.HttpClientName, client =>
        {
            client.BaseAddress = WithdrawalProviderClient.DefaultApiUri;
        });
        services.AddSingleton<WithdrawalProviderService>();

        base.Execute(services);
    }
}
