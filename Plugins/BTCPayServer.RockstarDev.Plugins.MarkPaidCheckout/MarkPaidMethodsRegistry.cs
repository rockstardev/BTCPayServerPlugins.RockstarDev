using System;
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Payments;

namespace BTCPayServer.RockstarDev.Plugins.MarkPaidCheckout;

public class MarkPaidMethodsRegistry
{
    public IReadOnlyList<string> Methods { get; }
    public IReadOnlyList<PaymentMethodId> PaymentMethodIds { get; }

    public MarkPaidMethodsRegistry(IEnumerable<string> methods)
    {
        var list = (methods ?? Array.Empty<string>())
            .Select(m => (m ?? string.Empty).Trim())
            .Where(m => !string.IsNullOrEmpty(m))
            .Select(m => m.ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (list.Count == 0)
            list.Add("CASH");
        Methods = list;
        PaymentMethodIds = list.Select(m => new PaymentMethodId(m)).ToList();
    }
}
