using System.ComponentModel.DataAnnotations;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Data.Models;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Services.Helpers;
using Xunit;

namespace BTCPayServer.Plugins.Tests.VendorPayTests;

public class EnumExtensionsTests
{
    [Theory]
    [InlineData(VendorPayInvoiceState.AwaitingApproval, "Awaiting Approval")]
    [InlineData(VendorPayInvoiceState.AwaitingPayment, "Awaiting Payment")]
    [InlineData(VendorPayInvoiceState.InProgress, "In Progress")]
    [InlineData(VendorPayInvoiceState.Completed, "Completed")]
    [InlineData(VendorPayInvoiceState.Cancelled, "Cancelled")]
    public void GetDisplayName_ReadsDisplayAttribute(VendorPayInvoiceState value, string expected)
    {
        Assert.Equal(expected, value.GetDisplayName());
    }

    [Fact]
    public void GetDisplayName_FallsBackToEnumName_WhenNoDisplayAttribute()
    {
        Assert.Equal(nameof(BareEnum.Foo), BareEnum.Foo.GetDisplayName());
        Assert.Equal(nameof(BareEnum.Bar), BareEnum.Bar.GetDisplayName());
    }

    [Fact]
    public void GetDisplayName_FallsBackToEnumName_WhenDisplayAttributeHasNullName()
    {
        Assert.Equal(nameof(MixedEnum.Tagged), MixedEnum.Tagged.GetDisplayName());
        Assert.Equal("Friendly", MixedEnum.WithName.GetDisplayName());
    }

    private enum BareEnum
    {
        Foo,
        Bar
    }

    private enum MixedEnum
    {
        [Display]
        Tagged,
        [Display(Name = "Friendly")]
        WithName
    }
}
