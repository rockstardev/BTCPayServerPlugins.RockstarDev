using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Rating;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Reporting;
using BTCPayServer.Services.Stores;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Services;

public class VendorPayReportProvider(
    PluginDbContextFactory pluginDbContextFactory,
    WalletRepository walletRepository,
    StoreRepository storeRepository,
    DisplayFormatter displayFormatter,
    CurrencyNameTable currencyNameTable) : ReportProvider
{
    public override string Name  => "Vendor Pay";
    
    
    public override async Task Query(QueryContext queryContext, CancellationToken cancellation)
    {
        var ctx = pluginDbContextFactory.CreateContext();
        
        queryContext.ViewDefinition = new ViewDefinition()
        {
            Fields =
            {
                new ("CreatedDate", "datetime"),
                new ("TransactionDate", "datetime"),
                new ("Name", "string"),
                new ("InvoiceDesc", "string"),
                new ("Address", "string"),
                new ("Currency", "string"),
                new ("Amount", "amount"),
                
                // Rate
                new ("Rate", "amount"),
                new ("PaymentCurrency", "string"),
                new ("PaymentAmount", "amount"),
                
                new ("TransactionId", "text"),
                new ("PaidInWallet", "boolean")
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
        var trackedCurrencies = store?.GetStoreBlob().GetTrackedRates() ?? new();
        foreach (var curr in trackedCurrencies)
        {
            queryContext.ViewDefinition.Fields.Add(new($"Rate ({curr})", "amount"));
        }

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
                Convert.ToDecimal(invoice.BtcPaid) is > 0 and {} btcPaid)
            {
                decimal usdRate = Convert.ToDecimal(invoice.Amount) / btcPaid;
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
                var rate = rateBook?.TryGetRate(new(paymentCurrency, curr));
                r.Add(rate is null ? null : displayFormatter.ToFormattedAmount(rate.Value, curr));
            }
        }
    }
}
