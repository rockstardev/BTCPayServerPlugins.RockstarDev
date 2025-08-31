using System;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Services.Rates;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.RockstarDev.Plugins.MarkPaidCheckout.PaymentHandlers;

public class MarkPaidPaymentMethodHandler(CurrencyNameTable currencyNameTable, PaymentMethodId paymentMethodId) : IPaymentMethodHandler
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

    public object ParsePaymentPromptDetails(JToken details) => details.ToObject<MarkPaidPaymentMethodDetails>(Serializer)!;

    public object ParsePaymentMethodConfig(JToken config) =>
        config.ToObject<MarkPaidPaymentMethodConfig>(Serializer) ?? throw new FormatException($"Invalid {nameof(MarkPaidPaymentMethodHandler)}");

    public object ParsePaymentDetails(JToken details) =>
        details.ToObject<MarkPaidPaymentData>(Serializer) ?? throw new FormatException($"Invalid {nameof(MarkPaidPaymentMethodHandler)}");
}

public class MarkPaidPaymentData { }
public class MarkPaidPaymentMethodConfig { }
public class MarkPaidPaymentMethodDetails { }
