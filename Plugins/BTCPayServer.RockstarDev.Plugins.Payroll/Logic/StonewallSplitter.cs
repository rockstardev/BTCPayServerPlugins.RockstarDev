using System;
using System.Collections.Generic;
using System.Linq;
using NBitcoin;

namespace BTCPayServer.RockstarDev.Plugins.VendorPay.Logic;

public record StonewallSplitInput(string InvoiceId, long Sats, string Destination, IReadOnlyList<string> ExtraAddresses);

public record StonewallSplitOutput(string InvoiceId, string Address, long Sats);

// Plans Stonewall-style split payouts for a batch of invoices. A single satoshi
// chunk size is picked for the whole batch: the largest per-invoice minimum
// chunk, where an invoice's minimum chunk is ceil(sats / address count) and the
// address count is the destination plus the vendor-supplied extra addresses.
// Each invoice is then paid in ceil(sats / chunk) outputs of that size (the
// last chunk takes the remainder) spread across its addresses, destination
// first. Invoices that cannot split (no extras, or chunks would fall below the
// dust limit) degrade to a single plain output for the full amount, so every
// invoice's outputs always sum exactly to its expected total.
public static class StonewallSplitter
{
    public const int MaxExtraAddresses = 5;
    public const long MinChunkSats = 546;

    public static bool TryParseExtraAddresses(string raw, string destination, Network network,
        out List<string> addresses, out string error)
    {
        addresses = new List<string>();
        error = null;

        foreach (var candidate in SplitStoredExtras(raw))
        {
            if (string.Equals(candidate, destination, StringComparison.OrdinalIgnoreCase))
            {
                error = "An extra address cannot be the same as the destination address.";
                return false;
            }

            if (addresses.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                continue;

            try
            {
                _ = BitcoinAddress.Create(candidate, network);
            }
            catch
            {
                error = $"Invalid Bitcoin address: {candidate}";
                return false;
            }

            addresses.Add(candidate);
        }

        if (addresses.Count > MaxExtraAddresses)
        {
            error = $"Too many extra addresses. Maximum is {MaxExtraAddresses}.";
            addresses = new List<string>();
            return false;
        }

        return true;
    }

    public static string[] SplitStoredExtras(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Array.Empty<string>();
        return raw.Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.Trim())
            .Where(e => e.Length > 0)
            .ToArray();
    }

    public static List<StonewallSplitOutput> PlanBatch(IReadOnlyList<StonewallSplitInput> invoices)
    {
        var outputs = new List<StonewallSplitOutput>();
        if (invoices == null || invoices.Count == 0)
            return outputs;

        static long CeilDiv(long a, long b) => (a + b - 1) / b;

        var chunk = invoices.Max(i => CeilDiv(i.Sats, 1 + (i.ExtraAddresses?.Count ?? 0)));

        foreach (var invoice in invoices)
        {
            var addresses = new List<string> { invoice.Destination };
            if (invoice.ExtraAddresses != null)
                addresses.AddRange(invoice.ExtraAddresses);

            var chunks = CeilDiv(invoice.Sats, chunk);
            chunks = Math.Min(chunks, addresses.Count);
            chunks = Math.Min(chunks, invoice.Sats / MinChunkSats);

            if (chunks < 2)
            {
                outputs.Add(new StonewallSplitOutput(invoice.InvoiceId, invoice.Destination, invoice.Sats));
                continue;
            }

            var perChunk = CeilDiv(invoice.Sats, chunks);
            for (var i = 0; i < chunks; i++)
            {
                var sats = i == chunks - 1 ? invoice.Sats - perChunk * (chunks - 1) : perChunk;
                outputs.Add(new StonewallSplitOutput(invoice.InvoiceId, addresses[i], sats));
            }
        }

        return outputs;
    }
}
