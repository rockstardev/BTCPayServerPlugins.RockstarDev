#nullable enable
using System;
using System.Net.Http;
using BTCPayServer.Lightning;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.RockstarDev.Plugins.LnurlSource.Data;
using Microsoft.Extensions.Logging;
using Network = NBitcoin.Network;

namespace BTCPayServer.RockstarDev.Plugins.LnurlSource;

public class LnurlSourceConnectionStringHandler : ILightningConnectionStringHandler
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly LnurlSourceDbContextFactory _dbContextFactory;

    public LnurlSourceConnectionStringHandler(
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory,
        LnurlSourceDbContextFactory dbContextFactory)
    {
        _httpClientFactory = httpClientFactory;
        _loggerFactory = loggerFactory;
        _dbContextFactory = dbContextFactory;
    }

    public ILightningClient? Create(string connectionString, Network network, out string? error)
    {
        var kv = LightningConnectionStringHelper.ExtractValues(connectionString, out var type);
        if (type != "lnurlverify")
        {
            error = null;
            return null;
        }

        if (!kv.TryGetValue("address", out var address) || string.IsNullOrWhiteSpace(address))
        {
            error = "The key 'address' is required (Lightning Address, e.g. user@domain.com)";
            return null;
        }

        address = address.Trim();

        // Validate Lightning Address format
        var parts = address.Split('@');
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
        {
            error = "Invalid Lightning Address format. Expected user@domain.com";
            return null;
        }

        error = null;
        var client = _httpClientFactory.CreateClient("lnurlverify");
        var logger = _loggerFactory.CreateLogger<LnurlSourceLightningClient>();
        return new LnurlSourceLightningClient(address, network, client, logger, _dbContextFactory);
    }
}
