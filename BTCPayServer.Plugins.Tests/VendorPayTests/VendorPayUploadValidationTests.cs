using System.Collections.Generic;
using System.IO;
using System.Linq;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Data.Models;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Services.Helpers;
using BTCPayServer.RockstarDev.Plugins.VendorPay.ViewModels;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace BTCPayServer.Plugins.Tests;

// An upload whose text did not fit its database column failed only at SaveChanges; the unhandled
// DbUpdateException ("value too long for type character varying(300)") made BTCPay disable the
// plugin and restart. ValidateLengths now rejects such values before anything is saved.
public class VendorPayUploadValidationTests
{
    private static ValidationResult Validate(VendorPayInvoiceUploadViewModel model)
    {
        var validation = new ValidationResult();
        VendorPayInvoiceUploadHelper.ValidateLengths(model, validation);
        return validation;
    }

    private static List<IFormFile> Files(int count)
        => Enumerable.Range(0, count)
            .Select(i => (IFormFile)new FormFile(new MemoryStream(), 0, 0, "ExtraFiles", $"receipt{i}.pdf"))
            .ToList();

    [Fact]
    public void ValuesAtTheColumnLimitsPass()
    {
        var result = Validate(new VendorPayInvoiceUploadViewModel
        {
            Description = new string('d', PayrollInvoice.DescriptionMaxLength),
            PurchaseOrder = new string('p', PayrollInvoice.PurchaseOrderMaxLength),
            Currency = new string('C', PayrollInvoice.CurrencyMaxLength),
            Destination = new string('a', PayrollInvoice.DestinationMaxLength),
            ExtraFiles = Files(VendorPayInvoiceUploadHelper.MaxExtraFiles)
        });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void MissingOptionalValuesPass()
    {
        Assert.True(Validate(new VendorPayInvoiceUploadViewModel { ExtraFiles = null }).IsValid);
    }

    [Fact]
    public void ADescriptionOneCharacterTooLongIsRejected()
    {
        var result = Validate(new VendorPayInvoiceUploadViewModel
        {
            Description = new string('d', PayrollInvoice.DescriptionMaxLength + 1)
        });
        Assert.False(result.IsValid);
        Assert.Contains("300", result.Errors[nameof(VendorPayInvoiceUploadViewModel.Description)]);
    }

    [Theory]
    [InlineData(nameof(VendorPayInvoiceUploadViewModel.PurchaseOrder))]
    [InlineData(nameof(VendorPayInvoiceUploadViewModel.Currency))]
    [InlineData(nameof(VendorPayInvoiceUploadViewModel.Destination))]
    public void EveryStoredFieldIsChecked(string field)
    {
        var model = new VendorPayInvoiceUploadViewModel();
        switch (field)
        {
            case nameof(model.PurchaseOrder):
                model.PurchaseOrder = new string('p', PayrollInvoice.PurchaseOrderMaxLength + 1);
                break;
            case nameof(model.Currency):
                model.Currency = new string('C', PayrollInvoice.CurrencyMaxLength + 1);
                break;
            case nameof(model.Destination):
                model.Destination = new string('a', PayrollInvoice.DestinationMaxLength + 1);
                break;
        }

        var result = Validate(model);
        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey(field));
    }

    [Fact]
    public void ExtraFilesAreCappedByTheColumnThatStoresTheirIds()
    {
        var max = VendorPayInvoiceUploadHelper.MaxExtraFiles;
        // max ids of 36 characters joined by commas fit the column; one more would not
        Assert.True(max * (PayrollInvoice.FileIdLength + 1) - 1 <= PayrollInvoice.ExtraFilenamesMaxLength);
        Assert.True((max + 1) * (PayrollInvoice.FileIdLength + 1) - 1 > PayrollInvoice.ExtraFilenamesMaxLength);

        var result = Validate(new VendorPayInvoiceUploadViewModel { ExtraFiles = Files(max + 1) });
        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey(nameof(VendorPayInvoiceUploadViewModel.ExtraFiles)));
    }
}
