using System.Net;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Services.Stores;
using BTCPayServer.Tests;
using NBitpayClient;
using Newtonsoft.Json.Linq;
using Xunit;

namespace BTCPayServer.Plugins.Tests;

[Collection("Plugin Tests")]
[Trait("Category", "PluginSecurityTest")]
public class MarkPaidSecurityTest : PlaywrightBaseTest
{
    private readonly SharedPluginTestFixture _fixture;

    public MarkPaidSecurityTest(SharedPluginTestFixture fixture, ITestOutputHelper helper) : base(helper)
    {
        _fixture = fixture;
        if (_fixture.ServerTester == null) _fixture.Initialize(this);
        ServerTester = _fixture.ServerTester;
    }

    public ServerTester ServerTester { get; }

    [Fact]
    public async Task AnonymousBuyerCannotSettleOnChainInvoiceWithBtcChain()
    {
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await ServerTester.ExplorerNode.GenerateAsync(1);
        await user.RegisterDerivationSchemeAsync("BTC", importKeysToNBX: true);

        var invoice = await CreateUsdInvoice(user, 10m);
        var entity = await ServerTester.PayTester.InvoiceRepository.GetInvoice(invoice.Id);
        Assert.Equal(InvoiceStatus.New, entity.Status);
        Assert.Empty(entity.GetPayments(false));

        var response = await PostMarkAsPaid(user.StoreId, invoice.Id, "BTC-CHAIN");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        entity = await ServerTester.PayTester.InvoiceRepository.GetInvoice(invoice.Id);
        Assert.Equal(InvoiceStatus.New, entity.Status);
        Assert.Empty(entity.GetPayments(false));
    }

    [Fact]
    public async Task AnonymousBuyerCannotSettleViaMethodStoreNeverEnabled()
    {
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await ServerTester.ExplorerNode.GenerateAsync(1);
        await user.RegisterDerivationSchemeAsync("BTC", importKeysToNBX: true);

        var invoice = await CreateUsdInvoice(user, 10m);

        // CASH is in the default MarkPaidMethodsRegistry, but this store never enabled it.
        var response = await PostMarkAsPaid(user.StoreId, invoice.Id, "CASH");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var entity = await ServerTester.PayTester.InvoiceRepository.GetInvoice(invoice.Id);
        Assert.Equal(InvoiceStatus.New, entity.Status);
        Assert.Empty(entity.GetPayments(false));
    }

    [Fact]
    public async Task ConfiguredMarkPaidMethodStillSettles()
    {
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await ServerTester.ExplorerNode.GenerateAsync(1);
        await user.RegisterDerivationSchemeAsync("BTC", importKeysToNBX: true);

        // Enable CASH on the store exactly like POST method/{method} (EnsureConfigEntry)
        // does, so new invoices carry a CASH payment prompt. The test project deliberately
        // has no reference to the plugin assembly, so we write the config entry as the
        // raw JToken the store blob stores (an empty config object).
        var storeRepository = ServerTester.PayTester.GetService<StoreRepository>();
        var store = await storeRepository.FindStore(user.StoreId);
        Assert.NotNull(store);
        store.SetPaymentMethodConfig(new PaymentMethodId("CASH"), new JObject());
        await storeRepository.UpdateStore(store);

        var invoice = await CreateUsdInvoice(user, 10m);
        var entity = await ServerTester.PayTester.InvoiceRepository.GetInvoice(invoice.Id);
        Assert.Equal(InvoiceStatus.New, entity.Status);
        Assert.NotNull(entity.GetPaymentPrompt(new PaymentMethodId("CASH")));

        var response = await PostMarkAsPaid(user.StoreId, invoice.Id, "CASH");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JObject.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json["success"]?.Value<bool>() ?? false);

        entity = await ServerTester.PayTester.InvoiceRepository.GetInvoice(invoice.Id);
        Assert.Equal(InvoiceStatus.Settled, entity.Status);
        var payment = Assert.Single(entity.GetPayments(false));
        Assert.Equal(PaymentStatus.Settled, payment.Status);
        Assert.Equal("CASH", payment.PaymentMethodId.ToString());
    }

    private async Task<Invoice> CreateUsdInvoice(TestAccount user, decimal amount)
    {
        return await user.BitPay.CreateInvoiceAsync(new Invoice
        {
            Buyer = new Buyer { email = "security-test@example.com" },
            Price = amount,
            Currency = "USD",
            FullNotifications = true
        }, Facade.Merchant);
    }

    private async Task<HttpResponseMessage> PostMarkAsPaid(string storeId, string invoiceId, string method)
    {
        // PayTester.HttpClient carries no cookies: this is an anonymous request.
        var request = new HttpRequestMessage(HttpMethod.Post,
            $"stores/{storeId}/markpaid/MarkAsPaid?invoiceId={invoiceId}&method={method}");
        request.Headers.Add("X-Requested-With", "RockstarHttpRequester");
        return await ServerTester.PayTester.HttpClient.SendAsync(request);
    }
}
