using System;
using System.Collections.Generic;
using System.Linq;
using NBitcoin;

namespace BTCPayServer.RockstarDev.Plugins.VendorPay.Logic;

public record StonewallSplitInput(string InvoiceId, long Sats, string Destination, IReadOnlyList<string> ExtraAddresses);

public record StonewallSplitOutput(string InvoiceId, string Address, long Sats);

public class StonewallBatchPlan(List<StonewallSplitOutput> outputs, long chunkSats, int decoyCount)
{
    public List<StonewallSplitOutput> Outputs { get; } = outputs;
    public long ChunkSats { get; } = chunkSats;
    public int DecoyCount { get; } = decoyCount;
}

// Plans Stonewall-style split payouts for a batch of invoices. A single satoshi
// chunk size is picked for the whole batch: the largest per-invoice minimum
// chunk (sats / address count). An invoice whose whole amount is below that
// size cannot split anyway and pays as one plain output, so its amount becomes
// the denomination the rest of the batch matches - this keeps small invoices
// whole instead of forcing every split to chunks larger than them.
//
// Each invoice with extra addresses is then paid in chunks of exactly that
// size across its addresses (destination first, one chunk per address), with
// any remainder emitted as one final smaller output. Invoices without extras
// are always paid whole. Every invoice's outputs sum exactly to its expected
// total. DecoyCount tells the caller how many sender-controlled outputs of
// ChunkSats to add (one per split invoice) so an observer cannot distinguish
// payment chunks from the sender's own coins returning to the wallet.
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

    public static StonewallBatchPlan PlanBatch(IReadOnlyList<StonewallSplitInput> invoices)
    {
        var outputs = new List<StonewallSplitOutput>();
        if (invoices == null || invoices.Count == 0)
            return new StonewallBatchPlan(outputs, 0, 0);

        static long CeilDiv(long a, long b) => (a + b - 1) / b;

        var chunk = invoices.Max(i => CeilDiv(i.Sats, 1 + (i.ExtraAddresses?.Count ?? 0)));
        var plainAmounts = invoices.Where(i => i.Sats < chunk).Select(i => i.Sats).ToList();
        if (plainAmounts.Count > 0)
            chunk = plainAmounts.Max();
        chunk = Math.Max(chunk, MinChunkSats);

        var decoys = 0;
        foreach (var invoice in invoices)
        {
            var addresses = new List<string> { invoice.Destination };
            if (invoice.ExtraAddresses != null)
                addresses.AddRange(invoice.ExtraAddresses);

            var fullChunks = invoice.Sats / chunk;
            if (addresses.Count == 1 || fullChunks < 1)
            {
                outputs.Add(new StonewallSplitOutput(invoice.InvoiceId, invoice.Destination, invoice.Sats));
                continue;
            }

            var before = outputs.Count;
            var chunkCount = (int)Math.Min(fullChunks, addresses.Count);
            for (var i = 0; i < chunkCount; i++)
                outputs.Add(new StonewallSplitOutput(invoice.InvoiceId, addresses[i], chunk));

            var leftover = invoice.Sats - chunkCount * chunk;
            if (leftover >= MinChunkSats)
            {
                outputs.Add(new StonewallSplitOutput(invoice.InvoiceId, addresses[chunkCount % addresses.Count], leftover));
            }
            else if (leftover > 0)
            {
                var last = outputs[^1];
                outputs[^1] = last with { Sats = last.Sats + leftover };
            }

            if (outputs.Count - before >= 2)
                decoys++;
        }

        return new StonewallBatchPlan(outputs, chunk, decoys);
    }
}
