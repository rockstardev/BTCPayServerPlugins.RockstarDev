using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Data;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Data.Models;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Logic;
using BTCPayServer.RockstarDev.Plugins.VendorPay.ViewModels;
using NBitcoin;

namespace BTCPayServer.RockstarDev.Plugins.VendorPay.Services.Helpers;

public class VendorPayInvoiceUploadHelper(
    PluginDbContextFactory dbContextFactory,
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
            ExtraAddresses = model.ExtraAddresses,
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

        var network = networkProvider.GetNetwork<BTCPayNetwork>(VendorPayPluginConst.BTC_CRYPTOCODE);
        try
        {
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

        var pendingInvoices = dbPlugin.PayrollInvoices
            .Where(a => a.User.StoreId == storeId &&
                        a.State != VendorPayInvoiceState.Completed && a.State != VendorPayInvoiceState.Cancelled)
            .Select(a => new { a.Destination, a.ExtraAddresses })
            .ToList();

        if (pendingInvoices.Any(a => a.Destination == model.Destination))
            validation.AddError(nameof(model.Destination), "This destination is already specified for another invoice with payment in progress.");

        string extraAddresses = null;
        if (settings.StonewallEnabled && !string.IsNullOrWhiteSpace(model.ExtraAddresses))
        {
            if (!StonewallSplitter.TryParseExtraAddresses(model.ExtraAddresses, model.Destination, network.NBitcoinNetwork,
                    out var parsed, out var parseError))
            {
                validation.AddError(nameof(model.ExtraAddresses), parseError);
            }
            else if (parsed.Count > 0)
            {
                // Split payments are reconciled by summing outputs across an
                // invoice's addresses, so none of them may collide with an
                // address already in flight on another pending invoice.
                var taken = pendingInvoices
                    .SelectMany(a => new[] { a.Destination }.Concat(StonewallSplitter.SplitStoredExtras(a.ExtraAddresses)))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var collision = parsed.FirstOrDefault(taken.Contains) ??
                                (taken.Contains(model.Destination) ? model.Destination : null);
                if (collision != null)
                    validation.AddError(nameof(model.ExtraAddresses),
                        $"Address {collision} is already specified for another invoice with payment in progress.");
                else
                    extraAddresses = string.Join(",", parsed);
            }
        }

        if (!validation.IsValid)
            // triggering early return with validation errors
            return validation;

        var removeTrailingZeros = model.Amount % 1 == 0 ? (int)model.Amount : model.Amount;
        var dbPayrollInvoice = new PayrollInvoice
        {
            Amount = removeTrailingZeros,
            CreatedAt = DateTime.UtcNow,
            Currency = model.Currency,
            Destination = model.Destination,
            ExtraAddresses = extraAddresses,
            PurchaseOrder = model.PurchaseOrder,
            Description = model.Description,
            UserId = userId,
            State = VendorPayInvoiceState.AwaitingApproval
        };

        var adminset = await settingsRepository.GetSettingAsync<VendorPayPluginSettings>();
        if (model.Invoice != null)
        {
            var uploaded = await fileService.AddFile(model.Invoice, adminset!.AdminAppUserId);
            dbPayrollInvoice.InvoiceFilename = uploaded.Id;
        }

        if (model.ExtraFiles?.Count > 0)
        {
            var extraFiles = new List<string>();
            foreach (var invoice in model.ExtraFiles)
            {
                var extraFileUpload = await fileService.AddFile(invoice, adminset!.AdminAppUserId);
                extraFiles.Add(extraFileUpload.Id);
            }

            dbPayrollInvoice.ExtraFilenames = string.Join(",", extraFiles);
        }

        dbPlugin.Add(dbPayrollInvoice);
        await dbPlugin.SaveChangesAsync();

        return validation;
    }
}
