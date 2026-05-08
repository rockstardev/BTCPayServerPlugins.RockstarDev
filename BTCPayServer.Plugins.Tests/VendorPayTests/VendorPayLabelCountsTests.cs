using System.Linq;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Data.Models;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Services.Helpers;
using Xunit;

namespace BTCPayServer.Plugins.Tests.VendorPayTests;

public class VendorPayLabelCountsTests
{
    private record InvoiceFixture(string UserId, VendorPayInvoiceState State);

    [Fact]
    public void CountInvoicesByUser_CountsAllStates()
    {
        var invoices = new[]
        {
            new InvoiceFixture("u1", VendorPayInvoiceState.Completed),
            new InvoiceFixture("u1", VendorPayInvoiceState.Completed),
            new InvoiceFixture("u1", VendorPayInvoiceState.AwaitingApproval),
            new InvoiceFixture("u2", VendorPayInvoiceState.Completed),
            new InvoiceFixture("u3", VendorPayInvoiceState.AwaitingApproval),
            new InvoiceFixture("u4", VendorPayInvoiceState.Cancelled),
        };

        var counts = VendorPayLabelCounts.CountInvoicesByUser(invoices, i => i.UserId);

        Assert.Equal(3, counts["u1"]);
        Assert.Equal(1, counts["u2"]);
        Assert.Equal(1, counts["u3"]);
        Assert.Equal(1, counts["u4"]);
    }

    [Fact]
    public void LabelCount_AppearsForUsersWithoutAwaitingApproval_RegressionForPr132()
    {
        // PR #132 bug: labels were hidden in the vendor invoice filter dropdown
        // when none of the tagged users had AwaitingApproval invoices, because
        // the per-label Count was sourced only from awaitingByUser. After the
        // fix, count reflects total invoices regardless of state, so the label
        // appears as long as any invoice exists for any tagged user.
        var invoices = new[]
        {
            new InvoiceFixture("u1", VendorPayInvoiceState.Completed),
            new InvoiceFixture("u2", VendorPayInvoiceState.InProgress),
        };
        var labelUserIds = new[] { "u1", "u2" };

        var counts = VendorPayLabelCounts.CountInvoicesByUser(invoices, i => i.UserId);
        var labelCount = VendorPayLabelCounts.LabelCount(labelUserIds, counts);

        Assert.Equal(2, labelCount);
        Assert.True(labelCount > 0,
            "Label should appear in filter when tagged users have any invoices in any state");
    }

    [Fact]
    public void LabelCount_ZeroWhenNoInvoicesExistForTaggedUsers()
    {
        // Inverse case: a label whose tagged users have NO invoices at all
        // should still produce Count=0 and be filtered out by the controller's
        // .Where(l => l.Count > 0) guard. Confirms the fix doesn't accidentally
        // surface empty labels.
        var invoices = new[]
        {
            new InvoiceFixture("other-user", VendorPayInvoiceState.Completed),
        };
        var labelUserIds = new[] { "u1", "u2" };

        var counts = VendorPayLabelCounts.CountInvoicesByUser(invoices, i => i.UserId);
        var labelCount = VendorPayLabelCounts.LabelCount(labelUserIds, counts);

        Assert.Equal(0, labelCount);
    }

    [Fact]
    public void LabelCount_SkipsUnknownUserIdsWithoutThrowing()
    {
        var invoices = new[]
        {
            new InvoiceFixture("u1", VendorPayInvoiceState.Completed),
        };
        var labelUserIds = new[] { "u1", "u-unknown" };

        var counts = VendorPayLabelCounts.CountInvoicesByUser(invoices, i => i.UserId);
        var labelCount = VendorPayLabelCounts.LabelCount(labelUserIds, counts);

        Assert.Equal(1, labelCount);
    }
}
