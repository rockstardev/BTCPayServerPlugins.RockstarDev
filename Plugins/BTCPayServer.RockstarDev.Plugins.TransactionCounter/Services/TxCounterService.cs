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
using InvoiceData = BTCPayServer.Data.InvoiceData;

namespace BTCPayServer.RockstarDev.Plugins.TransactionCounter.Services;

public class TxCounterService
{
    // Static cache variables
    private static InvoiceTransactionResult _cachedTransactionCount;
    private static DateTime _lastFetchTime = DateTime.MinValue;
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
        
        // updating the cache
        _cachedTransactionCount = await FetchAndCacheTransactionCount(model);
        _lastFetchTime = DateTime.UtcNow;

        return _cachedTransactionCount;
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
        InvoiceTransactionResult result = null;
        if (model.IncludeTransactionVolume)
        {
            await using var context = _applicationDbContextFactory.CreateContext();
            var invoices = new InvoiceQueryWrapper().GetInvoiceQuery(context, query);
            var transactions = invoices.ToArray();
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

            result = new InvoiceTransactionResult { TransactionCount = transactions.Count() + extra.TransactionCount, VolumeByCurrency = volumeByCurrency };
        }
        else
        {
            var transactionCount = await _invoiceRepository.GetInvoiceCount(query);
            var total = transactionCount + CalculateExtraTransactionCount(model).TransactionCount;

            result = new InvoiceTransactionResult { TransactionCount = total };
        }

        return result;
    }

    private InvoiceTransactionResult CalculateExtraTransactionCount(CounterPluginSettings model)
    {
        var result = new InvoiceTransactionResult
        {
            TransactionCount = 0, VolumeByCurrency = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        };
        if (string.IsNullOrWhiteSpace(model.ExtraTransactions))
            return result;

        try
        {
            var now = DateTime.UtcNow;
            var extraTransaction = JsonConvert.DeserializeObject<List<ExtraTransactionEntry>>(model.ExtraTransactions) ?? 
                                   new List<ExtraTransactionEntry>();
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

// This class has been extracted from the InvoiceRepository to avoid do tweaks
public class InvoiceQueryWrapper
{
    public IQueryable<InvoiceData> GetInvoiceQuery(ApplicationDbContext context, InvoiceQuery queryObject)
    {
        IQueryable<InvoiceData> query = queryObject.UserId is null
            ? context.Invoices
            : context.UserStore
                .Where(u => u.ApplicationUserId == queryObject.UserId)
                .SelectMany(c => c.StoreData.Invoices);

        if (!queryObject.IncludeArchived)
        {
            query = query.Where(i => !i.Archived);
        }

        if (queryObject.InvoiceId is { Length: > 0 })
        {
            if (queryObject.InvoiceId.Length > 1)
            {
                var idSet = queryObject.InvoiceId.ToHashSet().ToArray();
                query = query.Where(i => idSet.Contains(i.Id));
            }
            else
            {
                var invoiceId = queryObject.InvoiceId.First();
                query = query.Where(i => i.Id == invoiceId);
            }
        }

        if (queryObject.StoreId is { Length: > 0 })
        {
            if (queryObject.StoreId.Length > 1)
            {
                var stores = queryObject.StoreId.ToHashSet().ToArray();
                query = query.Where(i => stores.Contains(i.StoreDataId));
            }
            // Big performant improvement to use Where rather than Contains when possible
            // In our test, the first gives  720.173 ms vs 40.735 ms
            else
            {
                var storeId = queryObject.StoreId.First();
                query = query.Where(i => i.StoreDataId == storeId);
            }
        }

        if (!string.IsNullOrEmpty(queryObject.TextSearch))
        {
            var text = queryObject.TextSearch.Truncate(512);
#pragma warning disable CA1310 // Specify StringComparison
            query = query.Where(i => i.InvoiceSearchData.Any(data => data.Value.StartsWith(text)));
#pragma warning restore CA1310 // Specify StringComparison
        }

        if (queryObject.StartDate != null)
            query = query.Where(i => queryObject.StartDate.Value <= i.Created);

        if (queryObject.EndDate != null)
            query = query.Where(i => i.Created <= queryObject.EndDate.Value);

        if (queryObject.OrderId is { Length: > 0 })
        {
            if (queryObject.OrderId is [var orderId])
            {
                query = query.Where(i => InvoiceData.GetOrderId(i.Blob2) == orderId);
            }
            else
            {
                var orderIdSet = queryObject.OrderId.ToHashSet().ToArray();
                query = query.Where(i => orderIdSet.Contains(InvoiceData.GetOrderId(i.Blob2)));
            }
        }
        if (queryObject.ItemCode is { Length: > 0 })
        {
            if (queryObject.ItemCode is [var itemCode])
            {
                query = query.Where(i => InvoiceData.GetItemCode(i.Blob2) == itemCode);
            }
            else
            {
                var itemCodeSet = queryObject.ItemCode.ToHashSet().ToArray();
                query = query.Where(i => itemCodeSet.Contains(InvoiceData.GetItemCode(i.Blob2)));
            }
        }

        var statusSet = queryObject.Status is { Length: > 0 }
            ? queryObject.Status.Select(NormalizeStatus).Where(n => n is not null).ToHashSet()
            : new HashSet<string>();
        var exceptionStatusSet = queryObject.ExceptionStatus is { Length: > 0 }
            ? queryObject.ExceptionStatus.Select(NormalizeExceptionStatus).Where(n => n is not null).ToHashSet()
            : new HashSet<string>();

        if (statusSet.Any() || exceptionStatusSet.Any())
        {
            Expression<Func<InvoiceData, bool>> statusExpression = null;
            Expression<Func<InvoiceData, bool>> exceptionStatusExpression = null;
            if (statusSet.Count is 1)
            {
                var status = statusSet.First();
                statusExpression = i => i.Status == status;
            }
            else if (statusSet.Count is > 1)
            {
                statusExpression = i => statusSet.Contains(i.Status);
            }

            if (exceptionStatusSet.Count is 1)
            {
                var exceptionStatus = exceptionStatusSet.First();
                exceptionStatusExpression = i => i.ExceptionStatus == exceptionStatus;
            }
            else if (exceptionStatusSet.Count is > 1)
            {
                exceptionStatusExpression = i => exceptionStatusSet.Contains(i.ExceptionStatus);
            }
            var predicate = (statusExpression, exceptionStatusExpression) switch
            {
                ({ } a, { } b) => (Expression)Expression.Or(a.Body, b.Body),
                ({ } a, null) => a.Body,
                (null, { } b) => b.Body,
                _ => throw new NotSupportedException()
            };
            var expression = Expression.Lambda<Func<InvoiceData, bool>>(predicate, Expression.Parameter(typeof(InvoiceData), "i"));
            expression = expression.ReplaceParameterRef();
            query = query.Where(expression);
        }

        if (queryObject.Unusual != null)
        {
            var unusual = queryObject.Unusual.Value;
            query = query.Where(i => unusual == (i.Status == "Invalid" || !string.IsNullOrEmpty(i.ExceptionStatus)));
        }

        if (queryObject.OrderByDesc)
            query = query.OrderByDescending(q => q.Created);
        else
            query = query.OrderBy(q => q.Created);

        if (queryObject.Skip != null)
            query = query.Skip(queryObject.Skip.Value);

        if (queryObject.Take != null)
            query = query.Take(queryObject.Take.Value);

        return query;
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
