using System.Collections.Generic;
using System.Linq;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Logic;
using NBitcoin;
using Xunit;

namespace BTCPayServer.Plugins.Tests;

// Unit coverage for the Stonewall split planner. A payout batch picks a single
// satoshi chunk size for the whole selection (the largest per-invoice minimum
// chunk, where an invoice's minimum chunk is ceil(sats / address count)) and
// pays each invoice in that many chunks across its destination plus any extra
// addresses the vendor supplied at upload. Invoices that cannot split (no
// extra addresses, or a chunk would be dust) fall back to a single output for
// the full amount. Output sums per invoice must always equal the invoice total.
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
        var outputs = StonewallSplitter.PlanBatch(new[]
        {
            Input("i1", 100_000_000, Dest),
            Input("i2", 50_000_000, AddrA)
        });

        Assert.Equal(2, outputs.Count);
        Assert.Equal((100_000_000, Dest), (outputs[0].Sats, outputs[0].Address));
        Assert.Equal((50_000_000, AddrA), (outputs[1].Sats, outputs[1].Address));
    }

    [Fact]
    public void Plan_SingleInvoice_FiveAddresses_EqualSplit()
    {
        var extras = Addresses(4);
        var outputs = StonewallSplitter.PlanBatch(new[] { Input("i1", 100_000_000, Dest, extras.ToArray()) });

        Assert.Equal(5, outputs.Count);
        Assert.All(outputs, o => Assert.Equal(20_000_000, o.Sats));
        Assert.Equal(100_000_000, outputs.Sum(o => o.Sats));
        Assert.Equal(Dest, outputs[0].Address);
        Assert.Equal(extras, outputs.Skip(1).Select(o => o.Address).ToList());
    }

    [Fact]
    public void Plan_OwnerExample_OneBtcPlusHalfBtc_AllHalfBtcOutputs()
    {
        // 1 BTC invoice with 4 extra addresses + 0.5 BTC plain invoice.
        // The 0.5 BTC invoice sets the batch denominator, so the first invoice
        // is paid as 0.5 + 0.5 and the batch is three uniform 0.5 BTC outputs.
        var outputs = StonewallSplitter.PlanBatch(new[]
        {
            Input("i1", 100_000_000, Dest, Addresses(4).ToArray()),
            Input("i2", 50_000_000, AddrC)
        });

        Assert.Equal(3, outputs.Count);
        Assert.All(outputs, o => Assert.Equal(50_000_000, o.Sats));
        Assert.Equal(2, outputs.Count(o => o.InvoiceId == "i1"));
        Assert.Single(outputs.Where(o => o.InvoiceId == "i2"));
    }

    [Fact]
    public void Plan_ThreeAddresses_RemainderOnLastChunk()
    {
        var outputs = StonewallSplitter.PlanBatch(new[] { Input("i1", 100_000_000, Dest, AddrA, AddrB) });

        Assert.Equal(3, outputs.Count);
        Assert.Equal(33_333_334, outputs[0].Sats);
        Assert.Equal(33_333_334, outputs[1].Sats);
        Assert.Equal(33_333_332, outputs[2].Sats);
        Assert.Equal(100_000_000, outputs.Sum(o => o.Sats));
    }

    [Fact]
    public void Plan_ChunkCountClampedByAddressCount()
    {
        // Invoice A can only split 2 ways (1 extra), invoice B is plain 0.1 BTC.
        // Denominator comes from A: 0.5 BTC, so A pays 0.5 + 0.5 and B pays whole.
        var outputs = StonewallSplitter.PlanBatch(new[]
        {
            Input("a", 100_000_000, Dest, AddrA),
            Input("b", 10_000_000, AddrB)
        });

        Assert.Equal(3, outputs.Count);
        Assert.Equal(2, outputs.Count(o => o.InvoiceId == "a" && o.Sats == 50_000_000));
        Assert.Single(outputs.Where(o => o.InvoiceId == "b" && o.Sats == 10_000_000));
    }

    [Fact]
    public void Plan_TinyInvoice_NotSplitBelowDust()
    {
        var outputs = StonewallSplitter.PlanBatch(new[] { Input("i1", 1_000, Dest, Addresses(5).ToArray()) });

        Assert.Single(outputs);
        Assert.Equal(1_000, outputs[0].Sats);
        Assert.Equal(Dest, outputs[0].Address);
    }

    [Fact]
    public void Plan_LargePlainInvoice_SplitInvoiceFallsBackToPlain()
    {
        // A 5 BTC plain invoice forces the denominator above the 1 BTC invoice's
        // total, so the split cannot be constructed and degrades to plain payout.
        var outputs = StonewallSplitter.PlanBatch(new[]
        {
            Input("i1", 100_000_000, Dest, Addresses(4).ToArray()),
            Input("i2", 500_000_000, AddrC)
        });

        Assert.Equal(2, outputs.Count);
        Assert.Single(outputs.Where(o => o.InvoiceId == "i1" && o.Sats == 100_000_000 && o.Address == Dest));
        Assert.Single(outputs.Where(o => o.InvoiceId == "i2" && o.Sats == 500_000_000 && o.Address == AddrC));
    }

    [Fact]
    public void Plan_SplitUsesOnlyAsManyAddressesAsChunks()
    {
        // Denominator from a plain 0.5 BTC invoice -> 1 BTC invoice splits in 2,
        // so only the destination and the first extra address are used.
        var extras = Addresses(4);
        var outputs = StonewallSplitter.PlanBatch(new[]
        {
            Input("i1", 100_000_000, Dest, extras.ToArray()),
            Input("i2", 50_000_000, AddrC)
        });

        var i1Addresses = outputs.Where(o => o.InvoiceId == "i1").Select(o => o.Address).ToList();
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
        var outputs = StonewallSplitter.PlanBatch(new[] { Input("i1", sats, Dest, Addresses(extraCount).ToArray()) });

        Assert.Equal(sats, outputs.Sum(o => o.Sats));
        Assert.All(outputs, o => Assert.True(o.Sats > 0));
        Assert.All(outputs, o => Assert.True(o.Sats >= StonewallSplitter.MinChunkSats || outputs.Count == 1));
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
