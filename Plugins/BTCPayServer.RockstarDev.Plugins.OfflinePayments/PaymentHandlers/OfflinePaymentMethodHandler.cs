using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Services.Rates;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.RockstarDev.Plugins.OfflinePayments.PaymentHandlers;

public class OfflinePaymentMethodHandler(CurrencyNameTable currencyNameTable, PaymentMethodId paymentMethodId) : IPaymentMethodHandler
{
    public PaymentMethodId PaymentMethodId { get; } = paymentMethodId;

    public Task ConfigurePrompt(PaymentMethodContext context) => Task.CompletedTask;

    public Task BeforeFetchingRates(PaymentMethodContext context)
    {
        var currency = currencyNameTable.GetCurrencyData(context.InvoiceEntity.Currency, false);
        context.Prompt.Currency = currency.Code;
        context.Prompt.Divisibility = currency.Divisibility;
        context.Prompt.RateDivisibility = currency.Divisibility;
        return Task.CompletedTask;
    }

    public JsonSerializer Serializer { get; } = BlobSerializer.CreateSerializer().Serializer;
    public object ParsePaymentPromptDetails(JToken details) => new OfflinePaymentPromptDetails();
    public object ParsePaymentMethodConfig(JToken config) => new OfflinePaymentMethodConfig();
    public object ParsePaymentDetails(JToken details) => new OfflinePaymentData();
}

public class OfflinePaymentData { }
public class OfflinePaymentMethodConfig { }
public class OfflinePaymentPromptDetails { }
