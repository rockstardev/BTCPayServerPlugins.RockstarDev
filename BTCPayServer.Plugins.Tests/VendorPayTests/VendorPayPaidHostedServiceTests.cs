using System;
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Data.Models;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Services;
using Xunit;

namespace BTCPayServer.Plugins.Tests;

// Unit coverage for the payment-completion allocation helper. Verifies that
// only invoices whose expected satoshi amount is fully covered by observed
// output amounts are marked for completion, that a single output cannot
// satisfy multiple invoices sharing the same destination address, and that
// legacy rows with no expected amount do not complete on address-match alone.
public class VendorPayPaidHostedServiceTests
{
    private const string DestA = "bcrt1qzyzvsqjqn9xzzdgcqhp8c2k9fm5x2napw00v9d";
    private const string DestB = "bcrt1qs758ursh4q9z627kt3pp5yysm78ddny6txaqgw";

    private static PayrollInvoice Invoice(string id, string dest, long? amountSats, DateTimeOffset created)
        => new PayrollInvoice
        {
            Id = id,
            Destination = dest,
            AmountSats = amountSats,
            CreatedAt = created,
            State = VendorPayInvoiceState.AwaitingPayment
        };

    [Fact]
    public void NullAmountSats_LegacyInvoice_DoesNotComplete()
    {
        var invoices = new[] { Invoice("legacy", DestA, null, DateTimeOffset.UtcNow) };
        var observed = new Dictionary<string, long> { [DestA] = 5_000 };
        var completing = VendorPayPaidHostedService.SelectInvoicesToComplete(invoices, observed);
        Assert.Empty(completing);
    }

    [Fact]
    public void UnderPayment_DoesNotComplete()
    {
        var invoices = new[] { Invoice("i1", DestA, 10_000, DateTimeOffset.UtcNow) };
        var observed = new Dictionary<string, long> { [DestA] = 9_999 };
        var completing = VendorPayPaidHostedService.SelectInvoicesToComplete(invoices, observed);
        Assert.Empty(completing);
    }

    [Fact]
    public void DustOutput_DoesNotComplete()
    {
        var invoices = new[] { Invoice("i1", DestA, 10_000, DateTimeOffset.UtcNow) };
        var observed = new Dictionary<string, long> { [DestA] = 546 };
        var completing = VendorPayPaidHostedService.SelectInvoicesToComplete(invoices, observed);
        Assert.Empty(completing);
    }

    [Fact]
    public void ExactMatch_Completes()
    {
        var invoices = new[] { Invoice("i1", DestA, 10_000, DateTimeOffset.UtcNow) };
        var observed = new Dictionary<string, long> { [DestA] = 10_000 };
        var completing = VendorPayPaidHostedService.SelectInvoicesToComplete(invoices, observed);
        Assert.Single(completing);
        Assert.Equal("i1", completing[0].Id);
    }

    [Fact]
    public void OverPayment_CompletesOnce()
    {
        var invoices = new[] { Invoice("i1", DestA, 10_000, DateTimeOffset.UtcNow) };
        var observed = new Dictionary<string, long> { [DestA] = 15_000 };
        var completing = VendorPayPaidHostedService.SelectInvoicesToComplete(invoices, observed);
        Assert.Single(completing);
    }

    [Fact]
    public void SharedDestination_SingleOutputCoversOnlyOldest()
    {
        var older = Invoice("older", DestA, 10_000, DateTimeOffset.UtcNow.AddMinutes(-10));
        var newer = Invoice("newer", DestA, 10_000, DateTimeOffset.UtcNow);
        var observed = new Dictionary<string, long> { [DestA] = 10_000 };
        var completing = VendorPayPaidHostedService.SelectInvoicesToComplete(new[] { newer, older }, observed);
        Assert.Single(completing);
        Assert.Equal("older", completing[0].Id);
    }

    [Fact]
    public void SharedDestination_OutputCoversBoth_CompletesBoth()
    {
        var older = Invoice("older", DestA, 10_000, DateTimeOffset.UtcNow.AddMinutes(-10));
        var newer = Invoice("newer", DestA, 10_000, DateTimeOffset.UtcNow);
        var observed = new Dictionary<string, long> { [DestA] = 20_000 };
        var completing = VendorPayPaidHostedService.SelectInvoicesToComplete(new[] { newer, older }, observed);
        Assert.Equal(2, completing.Count);
        Assert.Equal(new[] { "older", "newer" }, completing.Select(i => i.Id).ToArray());
    }

    [Fact]
    public void SharedDestination_PartialBudget_StopsAtExhaustion()
    {
        var older = Invoice("older", DestA, 10_000, DateTimeOffset.UtcNow.AddMinutes(-10));
        var newer = Invoice("newer", DestA, 10_000, DateTimeOffset.UtcNow);
        var observed = new Dictionary<string, long> { [DestA] = 15_000 };
        var completing = VendorPayPaidHostedService.SelectInvoicesToComplete(new[] { newer, older }, observed);
        Assert.Single(completing);
        Assert.Equal("older", completing[0].Id);
    }

    [Fact]
    public void DistinctDestinations_IndependentBudgets()
    {
        var a = Invoice("a", DestA, 10_000, DateTimeOffset.UtcNow.AddMinutes(-1));
        var b = Invoice("b", DestB, 20_000, DateTimeOffset.UtcNow);
        var observed = new Dictionary<string, long> { [DestA] = 10_000, [DestB] = 20_000 };
        var completing = VendorPayPaidHostedService.SelectInvoicesToComplete(new[] { a, b }, observed);
        Assert.Equal(2, completing.Count);
    }

    [Fact]
    public void NoObservedForDestination_DoesNotComplete()
    {
        var invoices = new[] { Invoice("i1", DestA, 10_000, DateTimeOffset.UtcNow) };
        var observed = new Dictionary<string, long> { [DestB] = 10_000 };
        var completing = VendorPayPaidHostedService.SelectInvoicesToComplete(invoices, observed);
        Assert.Empty(completing);
    }

    [Fact]
    public void EmptyInputs_ReturnsEmpty()
    {
        var completing = VendorPayPaidHostedService.SelectInvoicesToComplete(
            Array.Empty<PayrollInvoice>(),
            new Dictionary<string, long>());
        Assert.Empty(completing);
    }

    [Fact]
    public void NullObservedMap_TreatedAsNoBudget()
    {
        var invoices = new[] { Invoice("i1", DestA, 10_000, DateTimeOffset.UtcNow) };
        var completing = VendorPayPaidHostedService.SelectInvoicesToComplete(invoices, null);
        Assert.Empty(completing);
    }
}
