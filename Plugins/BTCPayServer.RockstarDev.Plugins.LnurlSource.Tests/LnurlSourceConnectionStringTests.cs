#nullable enable
using System.Net.Http;
using BTCPayServer.RockstarDev.Plugins.LnurlSource;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Xunit;

namespace BTCPayServer.RockstarDev.Plugins.LnurlSource.Tests;

public class LnurlSourceConnectionStringTests
{
    private readonly LnurlSourceConnectionStringHandler _handler;

    public LnurlSourceConnectionStringTests()
    {
        var httpClientFactory = new TestHttpClientFactory();
        var loggerFactory = LoggerFactory.Create(b => { });
        _handler = new LnurlSourceConnectionStringHandler(httpClientFactory, loggerFactory);
    }

    [Fact]
    public void ValidConnectionString_ReturnsClient()
    {
        var client = _handler.Create(
            "type=lnurlverify;address=user@domain.com",
            Network.Main, out var error);

        Assert.NotNull(client);
        Assert.Null(error);
    }

    [Fact]
    public void MissingAddress_ReturnsError()
    {
        var client = _handler.Create(
            "type=lnurlverify",
            Network.Main, out var error);

        Assert.Null(client);
        Assert.NotNull(error);
        Assert.Contains("address", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EmptyAddress_ReturnsError()
    {
        var client = _handler.Create(
            "type=lnurlverify;address=",
            Network.Main, out var error);

        Assert.Null(client);
        Assert.NotNull(error);
    }

    [Fact]
    public void InvalidAddressFormat_ReturnsError()
    {
        var client = _handler.Create(
            "type=lnurlverify;address=notanemailformat",
            Network.Main, out var error);

        Assert.Null(client);
        Assert.NotNull(error);
        Assert.Contains("Invalid Lightning Address", error);
    }

    [Fact]
    public void WrongType_ReturnsNull()
    {
        var client = _handler.Create(
            "type=clightning;server=tcp://127.0.0.1:9835",
            Network.Main, out var error);

        Assert.Null(client);
        Assert.Null(error);
    }

    [Fact]
    public void ToString_RoundTrips()
    {
        var client = _handler.Create(
            "type=lnurlverify;address=bob@agi.cash",
            Network.Main, out _);

        Assert.NotNull(client);
        Assert.Equal("type=lnurlverify;address=bob@agi.cash", client.ToString());
    }

    private class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new HttpClient();
    }
}
