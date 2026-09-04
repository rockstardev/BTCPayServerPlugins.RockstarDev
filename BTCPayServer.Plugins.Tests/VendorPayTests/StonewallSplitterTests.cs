using System.Collections.Generic;
using System.Linq;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Logic;
using NBitcoin;
using Xunit;

namespace BTCPayServer.Plugins.Tests;

// Unit coverage for the Stonewall split planner. A payout batch picks a single
// satoshi chunk size for the whole selection: the largest per-invoice minimum
// chunk (sats / address count), unless some invoice's whole amount sits below
// that - such an invoice pays as one plain output and its amount becomes the
// denomination the rest of the batch matches. Each invoice with extra
// addresses is paid in chunks of exactly that size, one chunk per address
// (destination first), with any remainder as one final smaller output.
// Invoices without extras always pay whole. Output sums per invoice must
// always equal the invoice total. Once at least one invoice splits, DecoyCount
// is the number of chunk-sized outputs - the caller may add that many
// sender-controlled outputs of the chunk size so every payment chunk has a
// same-amount twin returning to the sender's own wallet (capped via settings).
public class StonewallSplitterTests
{
    private const string Dest = "bcrt1q6r39p8y4ye8j0xkqzhh6r6xh8mvrxnq3lygsdz";
    private const string AddrA = "bcrt1qzyzvsqjqn9xzzdgcqhp8c2k9fm5x2napw00v9d";
    private const string AddrB = "bcrt1qs758ursh4q9z627kt3pp5yysm78ddny6txaqgw";
    private const string AddrC = "bcrt1qne099wszrhzg4ungad0hnwgjm60euwmzfnxv3h";

    private static string NewAddress()
        => new Key().PubKey.WitHash.GetAddress(Network.RegTest).ToString();

    private static List<string> Addresses(int count)
        => Enumerable.Range(0, count).Select(_ => NewAddress()).ToList();

    private static StonewallSplitInput Input(string id, long sats, string dest, params string[] extras)
        => new(id, sats, dest, extras);

    // ----- TryParseExtraAddresses -----

    [Fact]
    public void Parse_NullOrBlank_ReturnsEmptyWithoutError()
    {
        Assert.True(StonewallSplitter.TryParseExtraAddresses(null, Dest, Network.RegTest, out var addresses, out var error));
        Assert.Empty(addresses);
        Assert.Null(error);

        Assert.True(StonewallSplitter.TryParseExtraAddresses("   ", Dest, Network.RegTest, out addresses, out error));
        Assert.Empty(addresses);
        Assert.Null(error);
    }

    [Fact]
    public void Parse_ValidList_TrimsAndParses()
    {
        var raw = $" {AddrA} , {AddrB}\n{AddrC} ";
        Assert.True(StonewallSplitter.TryParseExtraAddresses(raw, Dest, Network.RegTest, out var addresses, out var error));
        Assert.Null(error);
        Assert.Equal(new[] { AddrA, AddrB, AddrC }, addresses);
    }

    [Fact]
    public void Parse_DuplicateEntries_DedupedSilently()
    {
        var raw = $"{AddrA},{AddrA.ToUpperInvariant()},{AddrA}";
        Assert.True(StonewallSplitter.TryParseExtraAddresses(raw, Dest, Network.RegTest, out var addresses, out var error));
        Assert.Null(error);
        Assert.Single(addresses);
    }

    [Fact]
    public void Parse_SameAsDestination_Fails()
    {
        var raw = $"{AddrA},{Dest}";
        Assert.False(StonewallSplitter.TryParseExtraAddresses(raw, Dest, Network.RegTest, out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Parse_InvalidAddress_Fails()
    {
        var raw = $"{AddrA},not-an-address";
        Assert.False(StonewallSplitter.TryParseExtraAddresses(raw, Dest, Network.RegTest, out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Parse_WrongNetwork_Fails()
    {
        // AddrA is a regtest address; validating against mainnet must fail.
        Assert.False(StonewallSplitter.TryParseExtraAddresses(AddrA, Dest, Network.Main, out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Parse_MoreThanMax_Fails()
    {
        var raw = string.Join(",", Addresses(StonewallSplitter.MaxExtraAddresses + 1));
        Assert.False(StonewallSplitter.TryParseExtraAddresses(raw, Dest, Network.RegTest, out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Parse_ExactlyMax_Succeeds()
    {
        var extras = Addresses(StonewallSplitter.MaxExtraAddresses);
        Assert.True(StonewallSplitter.TryParseExtraAddresses(string.Join(",", extras), Dest, Network.RegTest, out var addresses, out var error));
        Assert.Null(error);
        Assert.Equal(StonewallSplitter.MaxExtraAddresses, addresses.Count);
    }

    // ----- PlanBatch -----

    [Fact]
    public void Plan_NoExtras_SingleOutputPerInvoice()
    {
        var plan = StonewallSplitter.PlanBatch(new[]
        {
            Input("i1", 100_000_000, Dest),
            Input("i2", 50_000_000, AddrA)
        });

        Assert.Equal(2, plan.Outputs.Count);
        Assert.Equal((100_000_000, Dest), (plan.Outputs[0].Sats, plan.Outputs[0].Address));
        Assert.Equal((50_000_000, AddrA), (plan.Outputs[1].Sats, plan.Outputs[1].Address));
        Assert.Equal(0, plan.DecoyCount);
    }

    [Fact]
    public void Plan_SingleInvoice_FiveAddresses_EqualSplit()
    {
        var extras = Addresses(4);
        var plan = StonewallSplitter.PlanBatch(new[] { Input("i1", 100_000_000, Dest, extras.ToArray()) });

        Assert.Equal(5, plan.Outputs.Count);
        Assert.All(plan.Outputs, o => Assert.Equal(20_000_000, o.Sats));
        Assert.Equal(100_000_000, plan.Outputs.Sum(o => o.Sats));
        Assert.Equal(Dest, plan.Outputs[0].Address);
        Assert.Equal(extras, plan.Outputs.Skip(1).Select(o => o.Address).ToList());
        Assert.Equal(20_000_000, plan.ChunkSats);
        Assert.Equal(5, plan.DecoyCount);
    }

    [Fact]
    public void Plan_OwnerExample_OneBtcPlusHalfBtc_AllHalfBtcOutputs()
    {
        // 1 BTC invoice with 4 extra addresses + 0.5 BTC plain invoice.
        // The 0.5 BTC invoice sets the batch denomination, so the first invoice
        // is paid as 0.5 + 0.5 and the batch is three uniform 0.5 BTC outputs.
        var plan = StonewallSplitter.PlanBatch(new[]
        {
            Input("i1", 100_000_000, Dest, Addresses(4).ToArray()),
            Input("i2", 50_000_000, AddrC)
        });

        Assert.Equal(3, plan.Outputs.Count);
        Assert.All(plan.Outputs, o => Assert.Equal(50_000_000, o.Sats));
        Assert.Equal(2, plan.Outputs.Count(o => o.InvoiceId == "i1"));
        Assert.Single(plan.Outputs.Where(o => o.InvoiceId == "i2"));
        Assert.Equal(3, plan.DecoyCount);
    }

    [Fact]
    public void Plan_SmallInvoiceStaysWhole_BigInvoiceMatchesItsDenomination()
    {
        // 0.1 BTC invoice with 2 extras batched with a 0.02 BTC invoice with 1
        // extra. The small invoice's whole amount (0.02) becomes the chunk size:
        // the big invoice pays 3 chunks of 0.02 (one per address) plus the
        // 0.04 remainder as a final output, the small invoice pays whole.
        //
        // That remainder wraps back onto the destination, which makes this the
        // only planner path that puts two outputs on one address. It is the shape
        // VendorPayPaidHostedService sums rather than assigns for, so the expected
        // array below is what stops a "simplification" of that += from silently
        // undercounting a fully paid invoice.
        //
        // "Only" also rests on the address list holding no duplicates, which is
        // enforced at write time by TryParseExtraAddresses - it rejects an extra
        // equal to the destination and skips repeats case-insensitively. Plan time
        // re-splits the stored string and filters only for address validity, so
        // relaxing that parser opens a second route to two outputs on one address.
        var plan = StonewallSplitter.PlanBatch(new[]
        {
            Input("big", 10_000_000, Dest, AddrA, AddrB),
            Input("small", 2_000_000, AddrC, NewAddress())
        });

        Assert.Equal(2_000_000, plan.ChunkSats);
        var big = plan.Outputs.Where(o => o.InvoiceId == "big").ToList();
        Assert.Equal(4, big.Count);
        Assert.Equal(new[] { (Dest, 2_000_000L), (AddrA, 2_000_000L), (AddrB, 2_000_000L), (Dest, 4_000_000L) },
            big.Select(o => (o.Address, o.Sats)).ToArray());
        var small = plan.Outputs.Where(o => o.InvoiceId == "small").ToList();
        Assert.Single(small);
        Assert.Equal((AddrC, 2_000_000L), (small[0].Address, small[0].Sats));
        // 4 chunk-sized outputs: big's 3 chunks + small's whole payment
        Assert.Equal(4, plan.DecoyCount);
    }

    [Fact]
    public void Plan_ThreeAddresses_RemainderOnLastChunk()
    {
        var plan = StonewallSplitter.PlanBatch(new[] { Input("i1", 100_000_000, Dest, AddrA, AddrB) });

        Assert.Equal(3, plan.Outputs.Count);
        Assert.Equal(33_333_334, plan.Outputs[0].Sats);
        Assert.Equal(33_333_334, plan.Outputs[1].Sats);
        Assert.Equal(33_333_332, plan.Outputs[2].Sats);
        Assert.Equal(100_000_000, plan.Outputs.Sum(o => o.Sats));
        // one chunk per address, remainder lands on the next unused address
        Assert.Equal(new[] { Dest, AddrA, AddrB }, plan.Outputs.Select(o => o.Address).ToArray());
    }

    [Fact]
    public void Plan_SmallPlainInvoice_SetsBatchDenomination()
    {
        // Invoice A has only 2 addresses, invoice B is a plain 0.1 BTC. B's whole
        // amount becomes the chunk: A pays 2 chunks of 0.1 plus the 0.8 remainder,
        // B pays whole - three uniform 0.1 BTC outputs in the batch.
        var plan = StonewallSplitter.PlanBatch(new[]
        {
            Input("a", 100_000_000, Dest, AddrA),
            Input("b", 10_000_000, AddrB)
        });

        Assert.Equal(10_000_000, plan.ChunkSats);
        Assert.Equal(4, plan.Outputs.Count);
        Assert.Equal(3, plan.Outputs.Count(o => o.Sats == 10_000_000));
        Assert.Single(plan.Outputs.Where(o => o.InvoiceId == "a" && o.Sats == 80_000_000));
        Assert.Equal(3, plan.DecoyCount);
    }

    [Fact]
    public void Plan_DustRemainder_FoldedIntoLastChunk()
    {
        var plan = StonewallSplitter.PlanBatch(new[]
        {
            Input("a", 11_100, Dest, AddrA),
            Input("b", 5_500, AddrB)
        });

        Assert.Equal(5_500, plan.ChunkSats);
        var a = plan.Outputs.Where(o => o.InvoiceId == "a").ToList();
        Assert.Equal(2, a.Count);
        Assert.Equal(5_500, a[0].Sats);
        Assert.Equal(5_600, a[1].Sats);
        Assert.Equal(11_100, a.Sum(o => o.Sats));
    }

    [Fact]
    public void Plan_TinyInvoice_NotSplitBelowDust()
    {
        var plan = StonewallSplitter.PlanBatch(new[] { Input("i1", 1_000, Dest, Addresses(5).ToArray()) });

        Assert.Single(plan.Outputs);
        Assert.Equal(1_000, plan.Outputs[0].Sats);
        Assert.Equal(Dest, plan.Outputs[0].Address);
        Assert.Equal(0, plan.DecoyCount);
    }

    [Fact]
    public void Plan_LargePlainInvoice_SplitInvoiceFallsBackToPlain()
    {
        // A 5 BTC plain invoice forces the denomination above the 1 BTC invoice's
        // total, so the split cannot be constructed and degrades to plain payout.
        var plan = StonewallSplitter.PlanBatch(new[]
        {
            Input("i1", 100_000_000, Dest, Addresses(4).ToArray()),
            Input("i2", 500_000_000, AddrC)
        });

        Assert.Equal(2, plan.Outputs.Count);
        Assert.Single(plan.Outputs.Where(o => o.InvoiceId == "i1" && o.Sats == 100_000_000 && o.Address == Dest));
        Assert.Single(plan.Outputs.Where(o => o.InvoiceId == "i2" && o.Sats == 500_000_000 && o.Address == AddrC));
        Assert.Equal(0, plan.DecoyCount);
    }

    [Fact]
    public void Plan_SplitUsesOnlyAsManyAddressesAsChunks()
    {
        // Denomination from a plain 0.5 BTC invoice -> 1 BTC invoice splits in 2,
        // so only the destination and the first extra address are used.
        var extras = Addresses(4);
        var plan = StonewallSplitter.PlanBatch(new[]
        {
            Input("i1", 100_000_000, Dest, extras.ToArray()),
            Input("i2", 50_000_000, AddrC)
        });

        var i1Addresses = plan.Outputs.Where(o => o.InvoiceId == "i1").Select(o => o.Address).ToList();
        Assert.Equal(new[] { Dest, extras[0] }, i1Addresses);
    }

    [Theory]
    [InlineData(100_000_000, 5)]
    [InlineData(100_000_000, 1)]
    [InlineData(99_999_999, 3)]
    [InlineData(54_321, 2)]
    [InlineData(5_461, 4)]
    public void Plan_OutputSumAlwaysEqualsInvoiceTotal(long sats, int extraCount)
    {
        var plan = StonewallSplitter.PlanBatch(new[] { Input("i1", sats, Dest, Addresses(extraCount).ToArray()) });

        Assert.Equal(sats, plan.Outputs.Sum(o => o.Sats));
        Assert.All(plan.Outputs, o => Assert.True(o.Sats > 0));
        Assert.All(plan.Outputs, o => Assert.True(o.Sats >= StonewallSplitter.MinChunkSats || plan.Outputs.Count == 1));
    }

    // ----- ComputeDecoyCount -----

    private static StonewallBatchPlan PlanWithSplitInvoices(int splitInvoices)
        => StonewallSplitter.PlanBatch(Enumerable.Range(0, splitInvoices)
            .Select(i => Input($"i{i}", 100_000_000, NewAddress(), NewAddress()))
            .ToList());

    [Fact]
    public void Decoys_BelowMinVendorOutputs_None()
    {
        var plan = PlanWithSplitInvoices(1); // 2 vendor outputs
        Assert.Equal(0, StonewallSplitter.ComputeDecoyCount(plan, minVendorOutputs: 3, maxDecoys: 5));
    }

    [Fact]
    public void Decoys_AtMinVendorOutputs_MatchesChunkOutputs()
    {
        var plan = PlanWithSplitInvoices(2); // 4 chunk-sized vendor outputs
        Assert.Equal(4, StonewallSplitter.ComputeDecoyCount(plan, minVendorOutputs: 2, maxDecoys: 5));
    }

    [Fact]
    public void Decoys_CappedAtMax()
    {
        var plan = PlanWithSplitInvoices(4);
        Assert.Equal(3, StonewallSplitter.ComputeDecoyCount(plan, minVendorOutputs: 1, maxDecoys: 3));
    }

    [Fact]
    public void Decoys_MaxZero_Disabled()
    {
        var plan = PlanWithSplitInvoices(2);
        Assert.Equal(0, StonewallSplitter.ComputeDecoyCount(plan, minVendorOutputs: 1, maxDecoys: 0));
    }

    [Fact]
    public void Decoys_NoSplitInvoices_None()
    {
        var plan = StonewallSplitter.PlanBatch(new[] { Input("i1", 100_000_000, Dest) });
        Assert.Equal(0, StonewallSplitter.ComputeDecoyCount(plan, minVendorOutputs: 0, maxDecoys: 5));
    }

    // ----- SplitStoredExtras -----

    [Fact]
    public void SplitStoredExtras_ParsesAndTrims()
    {
        Assert.Equal(new[] { AddrA, AddrB }, StonewallSplitter.SplitStoredExtras($" {AddrA} , {AddrB}"));
        Assert.Empty(StonewallSplitter.SplitStoredExtras(null));
        Assert.Empty(StonewallSplitter.SplitStoredExtras(""));
    }
}
