using System.Collections.Generic;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Controllers;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Data.Models;
using Xunit;

namespace BTCPayServer.Plugins.Tests;

// Unit tests for the decoy address selection helper used by the mass payment
// flow when the store's Stonewall option is enabled. These tests exercise
// parsing, rotation, validation, and defensive fallback paths without booting
// BTCPay or Playwright.
public class StonewallDecoyRotationTests
{
    private static readonly NBitcoin.Network Network = NBitcoin.Network.RegTest;

    private const string ValidRegtestAddrA = "bcrt1qzyzvsqjqn9xzzdgcqhp8c2k9fm5x2napw00v9d";
    private const string ValidRegtestAddrB = "bcrt1qs758ursh4q9z627kt3pp5yysm78ddny6txaqgw";
    private const string ValidRegtestAddrC = "bcrt1qne099wszrhzg4ungad0hnwgjm60euwmzfnxv3h";
    private const string InvoiceDestination = "bcrt1q6r39p8y4ye8j0xkqzhh6r6xh8mvrxnq3lygsdz";

    private static PayrollUser UserWith(string decoys) => new PayrollUser
    {
        Id = "user-1",
        Name = "Test User",
        StonewallDecoyAddresses = decoys
    };

    [Fact]
    public void NullUser_ReturnsNull()
    {
        var cursor = new Dictionary<string, int>();
        var result = VendorPayInvoiceController.SelectDecoyAddress(null, cursor, InvoiceDestination, Network);
        Assert.Null(result);
    }

    [Fact]
    public void EmptyDecoyList_ReturnsNull()
    {
        var user = UserWith(null);
        var cursor = new Dictionary<string, int>();
        Assert.Null(VendorPayInvoiceController.SelectDecoyAddress(user, cursor, InvoiceDestination, Network));

        user = UserWith("");
        Assert.Null(VendorPayInvoiceController.SelectDecoyAddress(user, cursor, InvoiceDestination, Network));

        user = UserWith("   ");
        Assert.Null(VendorPayInvoiceController.SelectDecoyAddress(user, cursor, InvoiceDestination, Network));
    }

    [Fact]
    public void SingleDecoy_ReturnsIt()
    {
        var user = UserWith(ValidRegtestAddrA);
        var cursor = new Dictionary<string, int>();
        var result = VendorPayInvoiceController.SelectDecoyAddress(user, cursor, InvoiceDestination, Network);
        Assert.Equal(ValidRegtestAddrA, result);
    }

    [Fact]
    public void CommaSeparated_RotatesAcrossMultipleCalls()
    {
        var user = UserWith($"{ValidRegtestAddrA},{ValidRegtestAddrB},{ValidRegtestAddrC}");
        var cursor = new Dictionary<string, int>();

        var first = VendorPayInvoiceController.SelectDecoyAddress(user, cursor, InvoiceDestination, Network);
        var second = VendorPayInvoiceController.SelectDecoyAddress(user, cursor, InvoiceDestination, Network);
        var third = VendorPayInvoiceController.SelectDecoyAddress(user, cursor, InvoiceDestination, Network);
        var fourth = VendorPayInvoiceController.SelectDecoyAddress(user, cursor, InvoiceDestination, Network);

        Assert.Equal(ValidRegtestAddrA, first);
        Assert.Equal(ValidRegtestAddrB, second);
        Assert.Equal(ValidRegtestAddrC, third);
        // Wraps around to first after exhausting the list
        Assert.Equal(ValidRegtestAddrA, fourth);
    }

    [Fact]
    public void NewlineSeparated_ParsesToo()
    {
        var user = UserWith($"{ValidRegtestAddrA}\n{ValidRegtestAddrB}");
        var cursor = new Dictionary<string, int>();
        Assert.Equal(ValidRegtestAddrA, VendorPayInvoiceController.SelectDecoyAddress(user, cursor, InvoiceDestination, Network));
        Assert.Equal(ValidRegtestAddrB, VendorPayInvoiceController.SelectDecoyAddress(user, cursor, InvoiceDestination, Network));
    }

    [Fact]
    public void DecoyEqualsDestination_SkipsIt()
    {
        var user = UserWith($"{InvoiceDestination},{ValidRegtestAddrA}");
        var cursor = new Dictionary<string, int>();
        var result = VendorPayInvoiceController.SelectDecoyAddress(user, cursor, InvoiceDestination, Network);
        Assert.Equal(ValidRegtestAddrA, result);
    }

    [Fact]
    public void DecoyEqualsDestination_CaseInsensitive_SkipsIt()
    {
        var user = UserWith($"{InvoiceDestination.ToUpperInvariant()},{ValidRegtestAddrA}");
        var cursor = new Dictionary<string, int>();
        var result = VendorPayInvoiceController.SelectDecoyAddress(user, cursor, InvoiceDestination, Network);
        Assert.Equal(ValidRegtestAddrA, result);
    }

    [Fact]
    public void MalformedAddress_IsSkipped()
    {
        var user = UserWith($"not-a-valid-address,{ValidRegtestAddrA}");
        var cursor = new Dictionary<string, int>();
        var result = VendorPayInvoiceController.SelectDecoyAddress(user, cursor, InvoiceDestination, Network);
        Assert.Equal(ValidRegtestAddrA, result);
    }

    [Fact]
    public void AllDecoysMalformed_ReturnsNull()
    {
        var user = UserWith("not-an-address,also-invalid,neither-is-this");
        var cursor = new Dictionary<string, int>();
        Assert.Null(VendorPayInvoiceController.SelectDecoyAddress(user, cursor, InvoiceDestination, Network));
    }

    [Fact]
    public void WhitespaceAroundEntries_IsTrimmed()
    {
        var user = UserWith($"  {ValidRegtestAddrA}  ,  {ValidRegtestAddrB}  ");
        var cursor = new Dictionary<string, int>();
        Assert.Equal(ValidRegtestAddrA, VendorPayInvoiceController.SelectDecoyAddress(user, cursor, InvoiceDestination, Network));
    }

    [Fact]
    public void MultipleVendorsInSameBatch_KeepIndependentCursors()
    {
        var vendorA = new PayrollUser { Id = "vendor-a", Name = "A", StonewallDecoyAddresses = $"{ValidRegtestAddrA},{ValidRegtestAddrB}" };
        var vendorB = new PayrollUser { Id = "vendor-b", Name = "B", StonewallDecoyAddresses = $"{ValidRegtestAddrC},{ValidRegtestAddrA}" };
        var cursor = new Dictionary<string, int>();

        var a1 = VendorPayInvoiceController.SelectDecoyAddress(vendorA, cursor, InvoiceDestination, Network);
        var b1 = VendorPayInvoiceController.SelectDecoyAddress(vendorB, cursor, InvoiceDestination, Network);
        var a2 = VendorPayInvoiceController.SelectDecoyAddress(vendorA, cursor, InvoiceDestination, Network);
        var b2 = VendorPayInvoiceController.SelectDecoyAddress(vendorB, cursor, InvoiceDestination, Network);

        Assert.Equal(ValidRegtestAddrA, a1);
        Assert.Equal(ValidRegtestAddrC, b1);
        Assert.Equal(ValidRegtestAddrB, a2);
        Assert.Equal(ValidRegtestAddrA, b2);
    }
}
