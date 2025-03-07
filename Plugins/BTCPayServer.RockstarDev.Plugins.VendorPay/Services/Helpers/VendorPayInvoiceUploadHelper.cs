using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Data;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Data.Models;
using BTCPayServer.RockstarDev.Plugins.VendorPay.ViewModels;
using NBitcoin;

namespace BTCPayServer.RockstarDev.Plugins.VendorPay.Services.Helpers;

public class VendorPayInvoiceUploadHelper(
    VendorPayPluginDbContextFactory dbContextFactory,
    IFileService fileService,
    ISettingsRepository settingsRepository,
    BTCPayNetworkProvider networkProvider)
{
    public Task<ValidationResult> Process(string storeId, string userId,
        PublicVendorPayInvoiceUploadViewModel model)
    {
        var mainModel = new VendorPayInvoiceUploadViewModel
        {
            Amount = model.Amount,
            Currency = model.Currency,
            Destination = model.Destination,
            Description = model.Description,
            Invoice = model.Invoice,
            PurchaseOrder = model.PurchaseOrder,
            ExtraFiles = model.ExtraFiles
        };
        return Process(storeId, userId, mainModel);
    }
    
    public async Task<ValidationResult> Process(string storeId, string userId, VendorPayInvoiceUploadViewModel model)
    {
        var validation = new ValidationResult();

        if (model.Amount <= 0)
            validation.AddError(nameof(model.Amount), "Amount must be more than 0.");

        try
        {
            var network = networkProvider.GetNetwork<BTCPayNetwork>(VendorPayPluginConst.BTC_CRYPTOCODE);
            Network.Parse<BitcoinAddress>(model.Destination, network.NBitcoinNetwork);
        }
        catch (Exception)
        {
            validation.AddError(nameof(model.Destination), "Invalid Destination, check format of address.");
        }

        await using var dbPlugin = dbContextFactory.CreateContext();
        var settings = await dbPlugin.GetSettingAsync(storeId);

        if (!settings.MakeInvoiceFilesOptional && model.Invoice == null)
            validation.AddError(nameof(model.Invoice), "Kindly include an invoice.");

        if (settings.PurchaseOrdersRequired && string.IsNullOrEmpty(model.PurchaseOrder))
            validation.AddError(nameof(model.PurchaseOrder), "Purchase Order is required.");

        var alreadyInvoiceWithAddress = dbPlugin.PayrollInvoices.Any(a =>
            a.Destination == model.Destination &&
            a.State != VendorPayInvoiceState.Completed && a.State != VendorPayInvoiceState.Cancelled);

        if (alreadyInvoiceWithAddress)
            validation.AddError(nameof(model.Destination), "This destination is already specified for another invoice with payment in progress.");

        if (!validation.IsValid)
        {
            // triggering early return with validation errors
            return validation;
        }

        var removeTrailingZeros = model.Amount % 1 == 0 ? (int)model.Amount : model.Amount;
        var dbVendorPayInvoice = new VendorPayInvoice
        {
            Amount = removeTrailingZeros,
            CreatedAt = DateTime.UtcNow,
            Currency = model.Currency,
            Destination = model.Destination,
            PurchaseOrder = model.PurchaseOrder,
            Description = model.Description,
            UserId = userId,
            State = VendorPayInvoiceState.AwaitingApproval
        };

        var adminset = await settingsRepository.GetSettingAsync<VendorPayPluginSettings>();
        if (model.Invoice != null)
        {
            var uploaded = await fileService.AddFile(model.Invoice, adminset!.AdminAppUserId);
            dbVendorPayInvoice.InvoiceFilename = uploaded.Id;
        }

        if (model.ExtraFiles?.Count > 0)
        {
            var extraFiles = new List<string>();
            foreach (var invoice in model.ExtraFiles)
            {
                var extraFileUpload = await fileService.AddFile(invoice, adminset!.AdminAppUserId);
                extraFiles.Add(extraFileUpload.Id);
            }
            dbVendorPayInvoice.ExtraFilenames = string.Join(",", extraFiles);
        }

        dbPlugin.Add(dbVendorPayInvoice);
        await dbPlugin.SaveChangesAsync();

        return validation;
    }
}
