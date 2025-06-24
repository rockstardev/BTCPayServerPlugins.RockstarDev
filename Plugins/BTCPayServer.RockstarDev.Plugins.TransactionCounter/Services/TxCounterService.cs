using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
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
    private readonly ApplicationDbContextFactory _applicationDbContextFactory;

    public TxCounterService(
        StoreRepository storeRepository,
        CurrencyNameTable currencyNameTable,
        InvoiceRepository invoiceRepository,
        ApplicationDbContextFactory applicationDbContextFactory)
    {
        _storeRepository = storeRepository;
        _currencyNameTable = currencyNameTable;
        _invoiceRepository = invoiceRepository;
        _applicationDbContextFactory = applicationDbContextFactory;
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
            IncludeArchived = model.IncludeArchived,
            Status = new[] { InvoiceStatus.Processing.ToString(), InvoiceStatus.Settled.ToString() },
            ExceptionStatus = new[] { InvoiceExceptionStatus.PaidLate.ToString(), InvoiceExceptionStatus.PaidOver.ToString() },
            StoreId = includedStoreIds.Length > 0 ? includedStoreIds : allStoreIds
        };
        var transactions = await GetInvoiceQuery(query);
        var volumeByCurrency = transactions
            .GroupBy(tx => tx.Currency.ToUpperInvariant())
            .ToDictionary(
                g => g.Key,
                g => g.Sum(tx => Math.Round(tx.Amount.Value, _currencyNameTable.GetNumberFormatInfo(tx.Currency)?.CurrencyDecimalDigits ?? 2)));

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

    private async Task<Data.InvoiceData[]> GetInvoiceQuery(InvoiceQuery queryObject)
    {
        await using var context = _applicationDbContextFactory.CreateContext();
        IQueryable<Data.InvoiceData> query = context.Invoices.Where(c => c.Amount > 0);
        if (!queryObject.IncludeArchived)
        {
            query = query.Where(i => !i.Archived);
        }
        if (queryObject.StoreId is { Length: > 0 })
        {
            if (queryObject.StoreId.Length > 1)
            {
                var stores = queryObject.StoreId.ToHashSet().ToArray();
                query = query.Where(i => stores.Contains(i.StoreDataId));
            }
            else
            {
                var storeId = queryObject.StoreId.First();
                query = query.Where(i => i.StoreDataId == storeId);
            }
        }
        if (queryObject.StartDate != null)
            query = query.Where(i => queryObject.StartDate.Value <= i.Created);

        if (queryObject.EndDate != null)
            query = query.Where(i => i.Created <= queryObject.EndDate.Value);

        var statusSet = queryObject.Status is { Length: > 0 }
            ? queryObject.Status.Select(NormalizeStatus).Where(n => n is not null).ToHashSet()
            : new HashSet<string>();

        var exceptionStatusSet = queryObject.ExceptionStatus is { Length: > 0 }
            ? queryObject.ExceptionStatus.Select(NormalizeExceptionStatus).Where(n => n is not null).ToHashSet()
            : new HashSet<string>();

        if (statusSet.Any() || exceptionStatusSet.Any())
        {
            Expression<Func<Data.InvoiceData, bool>> statusExpression = null;
            Expression<Func<Data.InvoiceData, bool>> exceptionStatusExpression = null;
            if (statusSet.Count is 1)
            {
                var status = statusSet.First();
                statusExpression = i => i.Status == status;
            }
            else if (statusSet.Count is > 1)
            {
                statusExpression = i => statusSet.Contains(i.Status);
            }
            var predicate = (statusExpression, exceptionStatusExpression) switch
            {
                ({ } a, { } b) => (Expression)Expression.Or(a.Body, b.Body),
                ({ } a, null) => a.Body,
                (null, { } b) => b.Body,
                _ => throw new NotSupportedException()
            };
            var expression = Expression.Lambda<Func<Data.InvoiceData, bool>>(predicate, Expression.Parameter(typeof(Data.InvoiceData), "i"));
            expression = expression.ReplaceParameterRef();
            query = query.Where(expression);
        }
        return query.ToArray();
    }

    private string NormalizeStatus(string status)
    {
        status = status.ToLowerInvariant();
        return status switch
        {
            "new" => "New",
            "paid" or "processing" => "Processing",
            "complete" or "confirmed" or "settled" => "Settled",
            "expired" => "Expired",
            "invalid" => "Invalid",
            _ => null
        };
    }

    private string NormalizeExceptionStatus(string status)
    {
        status = status.ToLowerInvariant();
        return status switch
        {
            "paidover" or "over" or "overpaid" => "PaidOver",
            "paidlate" or "late" => "PaidLate",
            "paidpartial" or "underpaid" or "partial" => "PaidPartial",
            "none" or "" => "",
            _ => null
        };
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
