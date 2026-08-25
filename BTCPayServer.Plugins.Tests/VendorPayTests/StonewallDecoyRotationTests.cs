using System.Collections.Generic;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Controllers;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Data.Models;
using Xunit;

namespace BTCPayServer.Plugins.Tests;

// Unit coverage for the decoy address selection helper. The helper is a pure
// static method that parses a comma/newline separated address list from the
// vendor profile, rejects malformed entries and destination collisions, and
// hands back one previously-unused address per vendor per batch. It fails
// closed on exhaustion (no wrap-around) so a single decoy is never reused
// within the same batch.
public class StonewallDecoyRotationTests
{
    private static readonly NBitcoin.Network Network = NBitcoin.Network.RegTest;

    private const string ValidRegtestAddrA = "bcrt1qzyzvsqjqn9xzzdgcqhp8c2k9fm5x2napw00v9d";
    private const string ValidRegtestAddrB = "bcrt1qs758ursh4q9z627kt3pp5yysm78ddny6txaqgw";
    private const string ValidRegtestAddrC = "bcrt1qne099wszrhzg4ungad0hnwgjm60euwmzfnxv3h";
    private const string InvoiceDestination = "bcrt1q6r39p8y4ye8j0xkqzhh6r6xh8mvrxnq3lygsdz";

    private static PayrollUser UserWith(string decoys, string id = "user-1") => new PayrollUser
    {
        Id = id,
        Name = "Test User",
        StonewallDecoyAddresses = decoys
    };

    [Fact]
    public void NullUser_ReturnsNull()
    {
        var used = new Dictionary<string, HashSet<string>>();
        Assert.Null(VendorPayInvoiceController.SelectDecoyAddress(null, used, InvoiceDestination, Network));
    }

    [Fact]
    public void EmptyDecoyList_ReturnsNull()
    {
        var used = new Dictionary<string, HashSet<string>>();
        Assert.Null(VendorPayInvoiceController.SelectDecoyAddress(UserWith(null), used, InvoiceDestination, Network));
        Assert.Null(VendorPayInvoiceController.SelectDecoyAddress(UserWith(""), used, InvoiceDestination, Network));
        Assert.Null(VendorPayInvoiceController.SelectDecoyAddress(UserWith("   "), used, InvoiceDestination, Network));
    }

    [Fact]
    public void SingleDecoy_ReturnsIt()
    {
        var user = UserWith(ValidRegtestAddrA);
        var used = new Dictionary<string, HashSet<string>>();
        Assert.Equal(ValidRegtestAddrA, VendorPayInvoiceController.SelectDecoyAddress(user, used, InvoiceDestination, Network));
    }

    [Fact]
    public void CommaSeparated_RotatesUniqueUntilExhausted()
    {
        var user = UserWith($"{ValidRegtestAddrA},{ValidRegtestAddrB},{ValidRegtestAddrC}");
        var used = new Dictionary<string, HashSet<string>>();

        var picks = new[]
        {
            VendorPayInvoiceController.SelectDecoyAddress(user, used, InvoiceDestination, Network),
            VendorPayInvoiceController.SelectDecoyAddress(user, used, InvoiceDestination, Network),
            VendorPayInvoiceController.SelectDecoyAddress(user, used, InvoiceDestination, Network),
        };
        Assert.Equal(new[] { ValidRegtestAddrA, ValidRegtestAddrB, ValidRegtestAddrC }, picks);
    }

    [Fact]
    public void Exhaustion_ReturnsNull_NoWrapAround()
    {
        var user = UserWith($"{ValidRegtestAddrA},{ValidRegtestAddrB}");
        var used = new Dictionary<string, HashSet<string>>();

        Assert.Equal(ValidRegtestAddrA, VendorPayInvoiceController.SelectDecoyAddress(user, used, InvoiceDestination, Network));
        Assert.Equal(ValidRegtestAddrB, VendorPayInvoiceController.SelectDecoyAddress(user, used, InvoiceDestination, Network));
        // Third call must NOT wrap around to A - fail closed.
        Assert.Null(VendorPayInvoiceController.SelectDecoyAddress(user, used, InvoiceDestination, Network));
        Assert.Null(VendorPayInvoiceController.SelectDecoyAddress(user, used, InvoiceDestination, Network));
    }

    [Fact]
    public void DuplicateEntriesInList_DedupedBeforeSelection()
    {
        var user = UserWith($"{ValidRegtestAddrA},{ValidRegtestAddrA},{ValidRegtestAddrB}");
        var used = new Dictionary<string, HashSet<string>>();
        Assert.Equal(ValidRegtestAddrA, VendorPayInvoiceController.SelectDecoyAddress(user, used, InvoiceDestination, Network));
        Assert.Equal(ValidRegtestAddrB, VendorPayInvoiceController.SelectDecoyAddress(user, used, InvoiceDestination, Network));
        Assert.Null(VendorPayInvoiceController.SelectDecoyAddress(user, used, InvoiceDestination, Network));
    }

    [Fact]
    public void DuplicateEntriesInList_CaseInsensitive()
    {
        var user = UserWith($"{ValidRegtestAddrA},{ValidRegtestAddrA.ToUpperInvariant()}");
        var used = new Dictionary<string, HashSet<string>>();
        Assert.Equal(ValidRegtestAddrA, VendorPayInvoiceController.SelectDecoyAddress(user, used, InvoiceDestination, Network));
        Assert.Null(VendorPayInvoiceController.SelectDecoyAddress(user, used, InvoiceDestination, Network));
    }

    [Fact]
    public void NewlineSeparated_ParsesToo()
    {
        var user = UserWith($"{ValidRegtestAddrA}\n{ValidRegtestAddrB}");
        var used = new Dictionary<string, HashSet<string>>();
        Assert.Equal(ValidRegtestAddrA, VendorPayInvoiceController.SelectDecoyAddress(user, used, InvoiceDestination, Network));
        Assert.Equal(ValidRegtestAddrB, VendorPayInvoiceController.SelectDecoyAddress(user, used, InvoiceDestination, Network));
    }

    [Fact]
    public void DecoyEqualsDestination_SkipsIt()
    {
        var user = UserWith($"{InvoiceDestination},{ValidRegtestAddrA}");
        var used = new Dictionary<string, HashSet<string>>();
        Assert.Equal(ValidRegtestAddrA, VendorPayInvoiceController.SelectDecoyAddress(user, used, InvoiceDestination, Network));
    }

    [Fact]
    public void DecoyEqualsDestination_CaseInsensitive_SkipsIt()
    {
        var user = UserWith($"{InvoiceDestination.ToUpperInvariant()},{ValidRegtestAddrA}");
        var used = new Dictionary<string, HashSet<string>>();
        Assert.Equal(ValidRegtestAddrA, VendorPayInvoiceController.SelectDecoyAddress(user, used, InvoiceDestination, Network));
    }

    [Fact]
    public void MalformedBeforeValid_SkipsMalformedAndReturnsValid()
    {
        var user = UserWith($"not-a-valid-address,{ValidRegtestAddrA}");
        var used = new Dictionary<string, HashSet<string>>();
        Assert.Equal(ValidRegtestAddrA, VendorPayInvoiceController.SelectDecoyAddress(user, used, InvoiceDestination, Network));
    }

    [Fact]
    public void AllDecoysMalformed_ReturnsNull()
    {
        var user = UserWith("not-an-address,also-invalid,neither-is-this");
        var used = new Dictionary<string, HashSet<string>>();
        Assert.Null(VendorPayInvoiceController.SelectDecoyAddress(user, used, InvoiceDestination, Network));
    }

    [Fact]
    public void WhitespaceAroundEntries_IsTrimmed()
    {
        var user = UserWith($"  {ValidRegtestAddrA}  ,  {ValidRegtestAddrB}  ");
        var used = new Dictionary<string, HashSet<string>>();
        Assert.Equal(ValidRegtestAddrA, VendorPayInvoiceController.SelectDecoyAddress(user, used, InvoiceDestination, Network));
        Assert.Equal(ValidRegtestAddrB, VendorPayInvoiceController.SelectDecoyAddress(user, used, InvoiceDestination, Network));
    }

    [Fact]
    public void MultipleVendorsInSameBatch_KeepIndependentUsedSets()
    {
        var vendorA = UserWith($"{ValidRegtestAddrA},{ValidRegtestAddrB}", id: "vendor-a");
        var vendorB = UserWith($"{ValidRegtestAddrC},{ValidRegtestAddrA}", id: "vendor-b");
        var used = new Dictionary<string, HashSet<string>>();

        Assert.Equal(ValidRegtestAddrA, VendorPayInvoiceController.SelectDecoyAddress(vendorA, used, InvoiceDestination, Network));
        Assert.Equal(ValidRegtestAddrC, VendorPayInvoiceController.SelectDecoyAddress(vendorB, used, InvoiceDestination, Network));
        Assert.Equal(ValidRegtestAddrB, VendorPayInvoiceController.SelectDecoyAddress(vendorA, used, InvoiceDestination, Network));
        // vendorB's used set is independent, so A is still available for vendorB even though vendorA already used it.
        Assert.Equal(ValidRegtestAddrA, VendorPayInvoiceController.SelectDecoyAddress(vendorB, used, InvoiceDestination, Network));
        // Both exhausted now.
        Assert.Null(VendorPayInvoiceController.SelectDecoyAddress(vendorA, used, InvoiceDestination, Network));
        Assert.Null(VendorPayInvoiceController.SelectDecoyAddress(vendorB, used, InvoiceDestination, Network));
    }
}
