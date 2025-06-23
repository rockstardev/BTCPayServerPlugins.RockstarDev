using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using NBXplorer.Models;
using Newtonsoft.Json;

namespace BTCPayServer.RockstarDev.Plugins.TransactionCounter.Services;

public class TxCounterService
{
    // Static cache variables
    private static InvoiceTransactionResult _cachedTransactionCount;
    private static DateTime _lastFetchTime = DateTime.MinValue;
    private static readonly object _lockObject = new();
    private static readonly TimeSpan _cacheExpiration = TimeSpan.FromMilliseconds(750);
    private readonly InvoiceRepository _invoiceRepository;
    private readonly StoreRepository _storeRepository;
    private readonly CurrencyNameTable _currencyNameTable;

    public TxCounterService(
        StoreRepository storeRepository,
        CurrencyNameTable currencyNameTable,
        InvoiceRepository invoiceRepository)
    {
        _storeRepository = storeRepository;
        _currencyNameTable = currencyNameTable;
        _invoiceRepository = invoiceRepository;
    }

    public async Task<InvoiceTransactionResult> GetTransactionCountAsync(CounterPluginSettings model)
    {
        // Check if we can use cached value
        var now = DateTime.UtcNow;
        if (now - _lastFetchTime < _cacheExpiration)
            return _cachedTransactionCount;

        // Need to refresh the cache
        lock (_lockObject)
        {
            // Double-check in case another thread updated while we were waiting
            if (now - _lastFetchTime < _cacheExpiration)
                return _cachedTransactionCount;

            // We need to refresh the cache
            return FetchAndCacheTransactionCount(model).GetAwaiter().GetResult();
        }
    }

    private async Task<InvoiceTransactionResult> FetchAndCacheTransactionCount(CounterPluginSettings model)
    {
        var stores = await _storeRepository.GetStores();
        var allStoreIds = stores.Where(c => !c.Archived).Select(s => s.Id).ToArray();
        var excludedStoreIds = (model.ExcludedStoreIds ?? "").Split(",", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var includedStoreIds = allStoreIds.Where(id => !excludedStoreIds.Contains(id)).ToArray();
        var query = new InvoiceQuery
        {
            StartDate = model.StartDate,
            EndDate = model.EndDate,
            Status = new[] { InvoiceStatus.Processing.ToString(), InvoiceStatus.Settled.ToString() },
            StoreId = includedStoreIds.Length > 0 ? includedStoreIds : allStoreIds
        };
        var transactions = await _invoiceRepository.GetInvoices(query);
        var volumeByCurrency = transactions
            .GroupBy(tx => tx.Currency.ToUpperInvariant())
            .ToDictionary(
                g => g.Key,
                g => g.Sum(tx => Math.Round(tx.PaidAmount.Net, _currencyNameTable.GetNumberFormatInfo(tx.Currency)?.CurrencyDecimalDigits ?? 2)));

        var extra = CalculateExtraTransactionCount(model);
        foreach (var kvp in extra.VolumeByCurrency)
        {
            if (volumeByCurrency.ContainsKey(kvp.Key))
                volumeByCurrency[kvp.Key] += kvp.Value;
            else
                volumeByCurrency[kvp.Key] = kvp.Value;
        }
        var result = new InvoiceTransactionResult
        {
            TransactionCount = transactions.Count() + extra.TransactionCount,
            VolumeByCurrency = volumeByCurrency
        };

        // Update the cache
        _cachedTransactionCount = result;
        _lastFetchTime = DateTime.UtcNow;

        return result;
    }

    private InvoiceTransactionResult CalculateExtraTransactionCount(CounterPluginSettings model)
    {
        var result = new InvoiceTransactionResult
        {
            TransactionCount = 0,
            VolumeByCurrency = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        };
        if (string.IsNullOrWhiteSpace(model.ExtraTransactions))
            return result;

        try
        {
            var now = DateTime.UtcNow;
            var extraTransaction = JsonConvert.DeserializeObject<List<ExtraTransactionEntry>>(model.ExtraTransactions) ?? new List<ExtraTransactionEntry>();
            foreach (var txn in extraTransaction)
            {
                if (now < txn.Start)
                    continue;

                var ratio = now >= txn.End
                    ? 1.0
                    : (now - txn.Start).TotalSeconds / (txn.End - txn.Start).TotalSeconds;

                result.TransactionCount += (int)(txn.Count * ratio);
                var amount = txn.Amount * (decimal)ratio;
                result.VolumeByCurrency.TryAdd(txn.Currency.ToUpperInvariant(), 0);
                result.VolumeByCurrency[txn.Currency.ToUpperInvariant()] += amount;
            }
            return result;
        }
        catch { return result; }
    }
}

public class ExtraTransactionEntry
{
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public int Count { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; }
}

public class InvoiceTransactionResult
{
    public int TransactionCount { get; set; }
    public Dictionary<string, decimal> VolumeByCurrency { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
