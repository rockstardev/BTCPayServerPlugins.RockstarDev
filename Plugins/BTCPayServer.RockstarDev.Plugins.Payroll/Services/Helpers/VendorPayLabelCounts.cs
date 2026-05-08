using System;
using System.Collections.Generic;
using System.Linq;

namespace BTCPayServer.RockstarDev.Plugins.VendorPay.Services.Helpers;

public static class VendorPayLabelCounts
{
    // Counts must include invoices in every state, not only AwaitingApproval -
    // see the regression test for PR #132 (label-filter dropdown hid labels when
    // tagged users had no invoices in that single state).
    public static IReadOnlyDictionary<string, int> CountInvoicesByUser<T>(
        IEnumerable<T> invoices,
        Func<T, string> userIdSelector)
    {
        return invoices
            .GroupBy(userIdSelector)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    public static int LabelCount(
        IEnumerable<string> userIdsForLabel,
        IReadOnlyDictionary<string, int> invoiceCountByUser)
    {
        return userIdsForLabel.Sum(uid => invoiceCountByUser.TryGetValue(uid, out var c) ? c : 0);
    }
}
