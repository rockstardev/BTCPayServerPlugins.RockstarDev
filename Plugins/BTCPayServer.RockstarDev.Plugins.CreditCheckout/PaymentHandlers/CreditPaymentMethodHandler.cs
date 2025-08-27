using System;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Services.Rates;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

#pragma warning disable CS8603 // Possible null reference return.

namespace BTCPayServer.RockstarDev.Plugins.CreditCheckout.PaymentHandlers;

public class CreditPaymentMethodHandler(CurrencyNameTable currencyNameTable) : IPaymentMethodHandler
{
    public PaymentMethodId PaymentMethodId => CreditCheckoutPlugin.CreditPmid;

    public Task ConfigurePrompt(PaymentMethodContext context)
    {
        return Task.CompletedTask;
    }

    public Task BeforeFetchingRates(PaymentMethodContext context)
    {
        var currency = currencyNameTable.GetCurrencyData(context.InvoiceEntity.Currency, false);

        context.Prompt.Currency = currency.Code;
        context.Prompt.Divisibility = currency.Divisibility;
        context.Prompt.RateDivisibility = currency.Divisibility;
        return Task.CompletedTask;
    }

    public JsonSerializer Serializer { get; } = BlobSerializer.CreateSerializer().Serializer;

    public object ParsePaymentPromptDetails(JToken details)
    {
        return details.ToObject<CreditPaymentMethodDetails>(Serializer);
    }

    public object ParsePaymentMethodConfig(JToken config)
    {
        return config.ToObject<CreditPaymentMethodConfig>(Serializer) ??
               throw new FormatException($"Invalid {nameof(CreditPaymentMethodHandler)}");
    }

    public object ParsePaymentDetails(JToken details)
    {
        return details.ToObject<CreditPaymentData>(Serializer) ??
               throw new FormatException($"Invalid {nameof(CreditPaymentMethodHandler)}");
    }
}

public class CreditPaymentData
{
}

public class CreditPaymentMethodConfig
{
}

public class CreditPaymentMethodDetails
{
}
