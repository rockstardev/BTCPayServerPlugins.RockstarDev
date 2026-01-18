using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Rating;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Data;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Reporting;
using BTCPayServer.Services.Stores;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.RockstarDev.Plugins.VendorPay.Services;

public class VendorPayReportProvider(
    PluginDbContextFactory pluginDbContextFactory,
    WalletRepository walletRepository,
    StoreRepository storeRepository,
    DisplayFormatter displayFormatter,
    CurrencyNameTable currencyNameTable) : ReportProvider
{
    public override string Name => "Vendor Pay";


    public override async Task Query(QueryContext queryContext, CancellationToken cancellation)
    {
        var ctx = pluginDbContextFactory.CreateContext();

        queryContext.ViewDefinition = new ViewDefinition
        {
            Fields =
            {
                new StoreReportResponse.Field("CreatedDate", "datetime"),
                new StoreReportResponse.Field("TransactionDate", "datetime"),
                new StoreReportResponse.Field("Name", "string"),
                new StoreReportResponse.Field("InvoiceDesc", "string"),
                new StoreReportResponse.Field("Address", "string"),
                new StoreReportResponse.Field("Currency", "string"),
                new StoreReportResponse.Field("Amount", "amount"),

                // Rate
                new StoreReportResponse.Field("Rate", "amount"),
                new StoreReportResponse.Field("PaymentCurrency", "string"),
                new StoreReportResponse.Field("PaymentAmount", "amount"),
                new StoreReportResponse.Field("TransactionId", "text"),
                new StoreReportResponse.Field("PaidInWallet", "boolean")
            }
        };

        var invoices = await ctx.PayrollInvoices
            .Include(a => a.User)
            .Where(a => a.User.StoreId == queryContext.StoreId && queryContext.From <= a.CreatedAt && a.CreatedAt < queryContext.To)
            .ToListAsync(cancellation);

        var walletId = new WalletId(queryContext.StoreId, "BTC");
        var txObjects = await walletRepository.GetWalletObjects(new GetWalletObjectsQuery
        {
            Ids = invoices.Select(d => d.TxnId).Where(t => t is not null).ToArray(),
            WalletId = walletId,
            Type = WalletObjectData.Types.Tx
        });

        var store = await storeRepository.FindStore(queryContext.StoreId);
        var trackedCurrencies = store?.GetStoreBlob().GetTrackedRates() ?? new HashSet<string>();
        foreach (var curr in trackedCurrencies)
            queryContext.ViewDefinition.Fields.Add(new StoreReportResponse.Field($"Rate ({curr})", "amount"));

        var rateBooks = txObjects
            .Select(t => (t.Key, RateBook.FromTxWalletObject(t.Value)))
            .Where(t => t.Item2 is not null)
            .ToDictionary(t => t.Key.Id, t => t.Item2);

        foreach (var invoice in invoices)
        {
            var desc = invoice.Description ?? "";
            if (!string.IsNullOrEmpty(invoice.PurchaseOrder))
                desc = $"{invoice.PurchaseOrder} - {desc}";

            var paymentCurrency = "BTC";
            var r = queryContext.AddData();
            r.Add(invoice.CreatedAt);
            r.Add(invoice.PaidAt);
            r.Add(invoice.User.Name);
            r.Add(desc);
            r.Add(invoice.Destination);
            r.Add(invoice.Currency);
            r.Add(displayFormatter.ToFormattedAmount(invoice.Amount, invoice.Currency));

            if (invoice.BtcPaid is not null &&
                Convert.ToDecimal(invoice.BtcPaid) is > 0 and { } btcPaid)
            {
                var usdRate = Convert.ToDecimal(invoice.Amount) / btcPaid;
                usdRate = usdRate.RoundToSignificant(currencyNameTable.GetCurrencyData(invoice.Currency, true).Divisibility);
                r.Add(displayFormatter.ToFormattedAmount(usdRate, invoice.Currency));
                r.Add(paymentCurrency);
                r.Add(displayFormatter.ToFormattedAmount(btcPaid, paymentCurrency));
            }
            else
            {
                r.Add(null);
                r.Add(null);
                r.Add(null);
            }

            r.Add(invoice.TxnId);
            r.Add(invoice.BtcPaid is not null);

            rateBooks.TryGetValue(invoice.TxnId ?? "unk", out var rateBook);
            foreach (var curr in trackedCurrencies)
            {
                var rate = rateBook?.TryGetRate(new CurrencyPair(paymentCurrency, curr));
                r.Add(rate is null ? null : displayFormatter.ToFormattedAmount(rate.Value, curr));
            }
        }
    }
}
