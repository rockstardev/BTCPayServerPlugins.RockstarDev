using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Services.Stores;

namespace BTCPayServer.RockstarDev.Plugins.WithdrawalProvider.Services;

public class WithdrawalProviderService
{
    private readonly StoreRepository _storeRepository;
    private readonly IHttpClientFactory _httpClientFactory;

    public WithdrawalProviderService(StoreRepository storeRepository, IHttpClientFactory httpClientFactory)
    {
        _storeRepository = storeRepository;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<WithdrawalProviderSettings> GetSettings(string storeId)
    {
        return await _storeRepository.GetSettingAsync<WithdrawalProviderSettings>(storeId, WithdrawalProviderSettings.SettingsName)
               ?? new WithdrawalProviderSettings();
    }

    public Task SaveSettings(string storeId, WithdrawalProviderSettings settings)
    {
        return _storeRepository.UpdateSetting(storeId, WithdrawalProviderSettings.SettingsName, settings);
    }

    public async Task<string> TestApiKey(string apiKey, CancellationToken cancellationToken = default)
    {
        var client = WithdrawalProviderClient.Create(_httpClientFactory, apiKey);
        return await client.GetUserId(cancellationToken);
    }

    public async Task<DashboardSnapshot> GetDashboardSnapshot(string storeId, CancellationToken cancellationToken = default)
    {
        var settings = await GetSettings(storeId);
        var client = WithdrawalProviderClient.Create(_httpClientFactory, settings.ApiKey);

        var now = DateTimeOffset.UtcNow;
        var start = now.AddDays(-30).ToUnixTimeMilliseconds();
        var end = now.ToUnixTimeMilliseconds();

        var userId = await client.GetUserId(cancellationToken);
        var rate = await client.GetRate(settings.Ticker, cancellationToken);
        var balance = await client.GetFiatBalance(settings.FiatCurrency, cancellationToken);
        var transactions = await client.GetTransactions(start, end, cancellationToken);

        return new DashboardSnapshot(userId, rate, balance, transactions);
    }

    public async Task<WithdrawalProviderClient.CreateOrderResponse> CreateOrder(string storeId,
        long sourceAmountSats,
        string ipAddress,
        string paymentMethod,
        CancellationToken cancellationToken = default)
    {
        var settings = await GetSettings(storeId);
        var client = WithdrawalProviderClient.Create(_httpClientFactory, settings.ApiKey);

        var request = new WithdrawalProviderClient.CreateOrderRequest(
            sourceAmountSats.ToString(),
            ipAddress,
            paymentMethod);

        return await client.CreateOrder(request, cancellationToken);
    }

    public record DashboardSnapshot(
        string UserId,
        WithdrawalProviderClient.RateResponse Rate,
        WithdrawalProviderClient.BalanceResponse Balance,
        WithdrawalProviderClient.GetTransactionsResponse Transactions);
}
