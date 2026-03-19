#nullable enable
using System.Net;
using System.Text;
using BTCPayServer.Lightning;
using BTCPayServer.RockstarDev.Plugins.LnurlSource;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Xunit;

namespace BTCPayServer.RockstarDev.Plugins.LnurlSource.Tests;

public class LnurlSourceLightningClientTests
{
    private readonly ILogger _logger;

    public LnurlSourceLightningClientTests()
    {
        _logger = LoggerFactory.Create(b => { }).CreateLogger<LnurlSourceLightningClient>();
    }

    [Fact]
    public async Task CreateInvoice_CallbackWithNoPr_Throws()
    {
        // Test that CreateInvoice correctly resolves LNURL-pay and calls callback,
        // and throws when the callback response has no valid payment request
        var payRequestJson = """
        {
            "callback": "https://domain.com/lnurlp/user/callback",
            "minSendable": 1000,
            "maxSendable": 100000000,
            "metadata": "[[\"text/plain\",\"Pay to user@domain.com\"]]",
            "tag": "payRequest"
        }
        """;

        var callbackJson = """
        {
            "pr": "",
            "verify": "https://domain.com/lnurlp/user/verify/somehash",
            "routes": []
        }
        """;

        var handler = new MockHttpHandler(new Dictionary<string, string>
        {
            ["https://domain.com/.well-known/lnurlp/user"] = payRequestJson,
            ["https://domain.com/lnurlp/user/callback?amount=10000"] = callbackJson
        });
        var httpClient = new HttpClient(handler);
        var client = new LnurlSourceLightningClient("user@domain.com", Network.RegTest, httpClient, _logger);

        // Empty pr field should throw
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await client.CreateInvoice(
                new CreateInvoiceParams(LightMoney.FromUnit(10, LightMoneyUnit.Satoshi), "test", TimeSpan.FromMinutes(5))));
    }

    [Fact]
    public async Task CreateInvoice_MissingCallback_Throws()
    {
        var payRequestJson = """
        {
            "minSendable": 1000,
            "maxSendable": 100000000,
            "metadata": "[[\"text/plain\",\"test\"]]",
            "tag": "payRequest"
        }
        """;

        var handler = new MockHttpHandler(new Dictionary<string, string>
        {
            ["https://domain.com/.well-known/lnurlp/user"] = payRequestJson
        });
        var httpClient = new HttpClient(handler);
        var client = new LnurlSourceLightningClient("user@domain.com", Network.RegTest, httpClient, _logger);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await client.CreateInvoice(
                new CreateInvoiceParams(LightMoney.FromUnit(10, LightMoneyUnit.Satoshi), "test", TimeSpan.FromMinutes(5))));
    }

    [Fact]
    public async Task GetInvoice_UnknownId_ReturnsNull()
    {
        var httpClient = new HttpClient(new MockHttpHandler(new Dictionary<string, string>()));
        var client = new LnurlSourceLightningClient("user@domain.com", Network.RegTest, httpClient, _logger);

        var invoice = await client.GetInvoice("unknown-id");
        Assert.Null(invoice);
    }

    [Fact]
    public async Task Validate_SuccessfulResolution()
    {
        var payRequestJson = """
        {
            "callback": "https://domain.com/lnurlp/user/callback",
            "minSendable": 1000,
            "maxSendable": 100000000,
            "metadata": "[[\"text/plain\",\"test\"]]",
            "tag": "payRequest"
        }
        """;

        var handler = new MockHttpHandler(new Dictionary<string, string>
        {
            ["https://domain.com/.well-known/lnurlp/user"] = payRequestJson
        });
        var httpClient = new HttpClient(handler);
        var client = new LnurlSourceLightningClient("user@domain.com", Network.RegTest, httpClient, _logger);

        var result = await client.Validate();
        Assert.Null(result); // null = valid
    }

    [Fact]
    public async Task Validate_UnresolvableAddress_ReturnsError()
    {
        var handler = new MockHttpHandler(new Dictionary<string, string>(), HttpStatusCode.NotFound);
        var httpClient = new HttpClient(handler);
        var client = new LnurlSourceLightningClient("user@nonexistent.com", Network.RegTest, httpClient, _logger);

        var result = await client.Validate();
        Assert.NotNull(result);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task Pay_ThrowsNotSupported()
    {
        var httpClient = new HttpClient(new MockHttpHandler(new Dictionary<string, string>()));
        var client = new LnurlSourceLightningClient("user@domain.com", Network.RegTest, httpClient, _logger);

        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await client.Pay("lnbc...", default));
    }

    [Fact]
    public async Task GetBalance_ThrowsNotSupported()
    {
        var httpClient = new HttpClient(new MockHttpHandler(new Dictionary<string, string>()));
        var client = new LnurlSourceLightningClient("user@domain.com", Network.RegTest, httpClient, _logger);

        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await client.GetBalance());
    }

    private class MockHttpHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _responses;
        private readonly HttpStatusCode _defaultStatus;

        public MockHttpHandler(Dictionary<string, string> responses, HttpStatusCode defaultStatus = HttpStatusCode.OK)
        {
            _responses = responses;
            _defaultStatus = defaultStatus;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";

            if (_responses.TryGetValue(url, out var body))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(_defaultStatus)
            {
                Content = new StringContent("{\"status\":\"ERROR\",\"reason\":\"Not found\"}", Encoding.UTF8, "application/json")
            });
        }
    }
}
