using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Newtonsoft.Json;

namespace BTCPayServer.RockstarDev.Plugins.TransactionCounter.Services;

public class TxCounterService
{
    // Static cache variables
    private static int _cachedTransactionCount;
    private static DateTime _lastFetchTime = DateTime.MinValue;
    private static readonly object _lockObject = new();
    private static readonly TimeSpan _cacheExpiration = TimeSpan.FromMilliseconds(750);
    private readonly InvoiceRepository _invoiceRepository;
    private readonly StoreRepository _storeRepository;

    public TxCounterService(
        StoreRepository storeRepository,
        InvoiceRepository invoiceRepository)
    {
        _storeRepository = storeRepository;
        _invoiceRepository = invoiceRepository;
    }

    public async Task<int> GetTransactionCountAsync(CounterPluginSettings model)
    {
        // Check if we can use cached value
        var now = DateTime.UtcNow;
        if (now - _lastFetchTime < _cacheExpiration) return _cachedTransactionCount;

        // Need to refresh the cache
        lock (_lockObject)
        {
            // Double-check in case another thread updated while we were waiting
            if (now - _lastFetchTime < _cacheExpiration) return _cachedTransactionCount;

            // We need to refresh the cache
            return FetchAndCacheTransactionCount(model).GetAwaiter().GetResult();
        }
    }

    private async Task<int> FetchAndCacheTransactionCount(CounterPluginSettings model)
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
        var transactionCount = await _invoiceRepository.GetInvoiceCount(query);
        var total = transactionCount + CalculateExtraTransactionCount(model);

        // Update the cache
        _cachedTransactionCount = total;
        _lastFetchTime = DateTime.UtcNow;

        return total;
    }

    private int CalculateExtraTransactionCount(CounterPluginSettings model)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(model.ExtraTransactions))
                return 0;

            var now = DateTime.UtcNow;
            var extraTransaction = JsonConvert.DeserializeObject<List<ExtraTransactionEntry>>(model.ExtraTransactions) ?? new List<ExtraTransactionEntry>();
            var total = 0;
            foreach (var txn in extraTransaction)
            {
                if (now < txn.Start)
                    continue;
                if (now >= txn.End)
                {
                    total += txn.Count;
                }
                else
                {
                    var duration = (txn.End - txn.Start).TotalSeconds;
                    var elapsed = (now - txn.Start).TotalSeconds;
                    var ratio = elapsed / duration;
                    total += (int)(txn.Count * ratio);
                }
            }

            return total;
        }
        catch { return 0; }
    }
}

public class ExtraTransactionEntry
{
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public int Count { get; set; }
}
