#nullable enable
using System;
using System.Net.Http;
using BTCPayServer.Lightning;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.RockstarDev.Plugins.LnurlVerify.Data;
using Microsoft.Extensions.Logging;
using Network = NBitcoin.Network;

namespace BTCPayServer.RockstarDev.Plugins.LnurlVerify;

public class LnurlVerifyConnectionStringHandler : ILightningConnectionStringHandler
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly LnurlVerifyDbContextFactory _dbContextFactory;

    public LnurlVerifyConnectionStringHandler(
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory,
        LnurlVerifyDbContextFactory dbContextFactory)
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
        var logger = _loggerFactory.CreateLogger<LnurlVerifyLightningClient>();
        return new LnurlVerifyLightningClient(address, network, client, logger, _dbContextFactory);
    }
}
