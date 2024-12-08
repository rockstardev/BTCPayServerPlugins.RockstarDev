using System;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.RockstarDev.Plugins.CashCheckoutMethod;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.RockstarDev.Plugins.CashCheckout.PaymentHandlers;

public class CashPaymentMethodHandler(
    CashCheckoutConfigurationItem configurationItem,
    CurrencyNameTable currencyNameTable,
    InvoiceRepository invoiceRepository) : IPaymentMethodHandler
{
    public PaymentMethodId PaymentMethodId { get; } = configurationItem.GetPaymentMethodId();
    public Task ConfigurePrompt(PaymentMethodContext context)
    {
        // if (!tronUSDtRpcProvider.IsAvailable(configurationItem.GetPaymentMethodId()))
        //     throw new PaymentMethodUnavailableException("Node or wallet not available");
        //
        // var details = new TronUSDtLikeOnChainPaymentMethodDetails();
        // var availableAddress = await ParsePaymentMethodConfig(context.PaymentMethodConfig)
        //                            .GetOneNotReservedAddress(context.PaymentMethodId, invoiceRepository) ??
        //                        throw new PaymentMethodUnavailableException("All your TRON addresses are currently waiting payment");
        // context.Prompt.Destination = availableAddress;
        // context.Prompt.PaymentMethodFee = 0; 
        // context.Prompt.Details = JObject.FromObject(details, Serializer);

        return Task.CompletedTask;
    }

    public Task BeforeFetchingRates(PaymentMethodContext context)
    {
        context.Prompt.Currency = configurationItem.Currency;
        context.Prompt.Divisibility = configurationItem.Divisibility;
        context.Prompt.RateDivisibility = currencyNameTable.GetCurrencyData(context.Prompt.Currency, false).Divisibility;
        return Task.CompletedTask;
    }

    public JsonSerializer Serializer { get; } = BlobSerializer.CreateSerializer().Serializer;
    public object ParsePaymentPromptDetails(JToken details)
    {
        return details.ToObject<CashPaymentMethodDetails>(Serializer);
    }

    public object ParsePaymentMethodConfig(JToken config)
    {
        return config.ToObject<CashPaymentMethodConfig>(Serializer) ??
               throw new FormatException($"Invalid {nameof(CashPaymentMethodHandler)}");
    }

    public object ParsePaymentDetails(JToken details)
    {
        return details.ToObject<CashPaymentData>(Serializer) ??
               throw new FormatException($"Invalid {nameof(CashPaymentMethodHandler)}");
    }
}

public class CashPaymentData
{
}

public class CashPaymentMethodConfig
{
}

public class CashPaymentMethodDetails
{
}