using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Rating;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data.Models;
using BTCPayServer.Services.Rates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Models.WalletViewModels;
using BTCPayServer.Services;
using BTCPayServer.Services.Labels;
using BTCPayServer.Services.Wallets;
using BTCPayServer.Configuration;
using BTCPayServer.RockstarDev.Plugins.Payroll.ViewModels;
using Microsoft.Extensions.Options;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Controllers;

[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class PayrollInvoiceController(
    PayrollPluginDbContextFactory payrollPluginDbContextFactory,
    DefaultRulesCollection defaultRulesCollection,
    RateFetcher rateFetcher,
    PaymentMethodHandlerDictionary handlers,
    BTCPayNetworkProvider networkProvider,
    IFileService fileService,
    IOptions<DataDirectories> dataDirectories,
    UserManager<ApplicationUser> userManager,
    ISettingsRepository settingsRepository,
    HttpClient httpClient,
    BTCPayWalletProvider walletProvider,
    WalletRepository walletRepository,
    LabelService labelService)
    : Controller
{
    private StoreData CurrentStore => HttpContext.GetStoreData();

    [HttpGet("~/plugins/{storeId}/payroll/list")]
    public async Task<IActionResult> List(string storeId, bool all)
    {
        await using var ctx = payrollPluginDbContextFactory.CreateContext();
        var payrollInvoices = await ctx.PayrollInvoices
            .Include(data => data.User)
            .Where(p => p.User.StoreId == storeId && !p.IsArchived)
            .OrderByDescending(data => data.CreatedAt).ToListAsync();

        if (!all)
        {
            payrollInvoices = payrollInvoices.Where(a => a.User.State == PayrollUserState.Active).ToList();
        }

        // triggering saving of admin user id if needed
        var adminset = await settingsRepository.GetSettingAsync<PayrollPluginSettings>();
        adminset ??= new PayrollPluginSettings();
        if (adminset.AdminAppUserId is null)
        {
            adminset.AdminAppUserId = userManager.GetUserId(User);
            await settingsRepository.UpdateSetting(adminset);
        }

        var settings = await ctx.GetSettingAsync(storeId);
        var model = new PayrollInvoiceListViewModel
        {
            All = all,
            PurchaseOrdersRequired = settings.PurchaseOrdersRequired,
            PayrollInvoices = payrollInvoices.Select(tuple => new PayrollInvoiceViewModel
            {
                CreatedAt = tuple.CreatedAt,
                Id = tuple.Id,
                Name = tuple.User.Name,
                Email = tuple.User.Email,
                Destination = tuple.Destination,
                Amount = tuple.Amount,
                Currency = tuple.Currency,
                State = tuple.State,
                TxnId = tuple.TxnId,
                PurchaseOrder = tuple.PurchaseOrder,
                Description = tuple.Description,
                InvoiceUrl = tuple.InvoiceFilename
            }).ToList()
        };
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> MassAction(string command, string[] selectedItems)
    {
        IActionResult NotSupported(string err)
        {
            TempData[WellKnownTempData.ErrorMessage] = err;
            return RedirectToAction(nameof(List), new { CurrentStore.Id });
        }
        if (selectedItems.Length == 0)
            return NotSupported("No invoice has been selected");

        var ctx = payrollPluginDbContextFactory.CreateContext();
        var invoices = ctx.PayrollInvoices
                            .Include(a => a.User)
                            .Where(a => selectedItems.Contains(a.Id))
                            .ToList();

        switch (command)
        {
            case "payinvoices":
                return await payInvoices(selectedItems);

            case "markpaid":
                invoices.ForEach(c => c.State = PayrollInvoiceState.Completed);
                ctx.SaveChanges();
                TempData.SetStatusMessageModel(new StatusMessageModel()
                {
                    Message = $"Invoices successfully marked as paid",
                    Severity = StatusMessageModel.StatusSeverity.Success
                });
                break;

            case "download":
                return await DownloadInvoicesAsZipAsync(invoices);
            
            case "export":
                return await ExportInvoices(invoices);
        }
        return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
    }

    private async Task<IActionResult> DownloadInvoicesAsZipAsync(List<PayrollInvoice> invoices)
    {
        var zipName = $"PayrollInvoices-{DateTime.Now:yyyy_MM_dd-HH_mm_ss}.zip";

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            var usedFilenames = new HashSet<string>();

            foreach (var invoice in invoices)
            {
                var fileUrl =
                    await fileService.GetFileUrl(HttpContext.Request.GetAbsoluteRootUri(), invoice.InvoiceFilename);
                var filename = Path.GetFileName(fileUrl);
                byte[] fileBytes;
                
                // if it is local storage, then read file from HDD
                if (fileUrl?.Contains("/LocalStorage/") == true)
                    fileBytes = await System.IO.File.ReadAllBytesAsync(Path.Combine(dataDirectories.Value.StorageDir, filename));
                else 
                    fileBytes = await httpClient.DownloadFileAsByteArray(fileUrl);
                
                // replace guid of invoice with name of the user + year-month
                if (filename?.Length > 36)
                {
                    var first36 = filename.Substring(0, 36);
                    if (Guid.TryParse(first36, out Guid result))
                    {
                        var newName = $"{invoice.User.Name} - {invoice.CreatedAt:yyyy-MM} ";
                        filename = filename.Replace(first36, newName);

                        // Ensure filename is unique
                        var baseFilename = Path.GetFileNameWithoutExtension(filename);
                        var extension = Path.GetExtension(filename);
                        int counter = 1;

                        while (usedFilenames.Contains(filename))
                        {
                            filename = $"{baseFilename} ({counter}){extension}";
                            counter++;
                        }

                        usedFilenames.Add(filename);
                    }
                }

                var entry = zip.CreateEntry(filename);
                using (var entryStream = entry.Open())
                {
                    await entryStream.WriteAsync(fileBytes, 0, fileBytes.Length);
                }
            }
        }

        ms.Position = 0;
        return File(ms.ToArray(), "application/zip", zipName);
    }

    private async Task<IActionResult> payInvoices(string[] selectedItems)
    {
        await using var ctx = payrollPluginDbContextFactory.CreateContext();
        var invoices = ctx.PayrollInvoices
            .Include(a => a.User)
            .Where(a => selectedItems.Contains(a.Id))
            .ToList();

        // initialize exchange rates
        var rates = new Dictionary<string, decimal>();
        var currencies = invoices.Select(a => a.Currency).Distinct().ToList();
        foreach (var currency in currencies)
        {
            if (currency == PayrollPluginConst.BTC_CRYPTOCODE)
            {
                rates.Add(currency, 1);
            }
            else
            {
                var rate = await rateFetcher.FetchRate(new CurrencyPair(currency, PayrollPluginConst.BTC_CRYPTOCODE),
                    CurrentStore.GetStoreBlob().GetRateRules(defaultRulesCollection), new StoreIdRateContext(CurrentStore.Id), CancellationToken.None);
                if (rate.BidAsk == null)
                {
                    throw new Exception("Currency is not supported");
                }

                rates.Add(currency, rate.BidAsk.Bid);
            }
        }

        var network = networkProvider.GetNetwork<BTCPayNetwork>(PayrollPluginConst.BTC_CRYPTOCODE);
        var bip21 = new List<string>();
        foreach (var invoice in invoices)
        {
            var satsAmount = Math.Ceiling(invoice.Amount * rates[invoice.Currency] * 100_000_000);
            var amountInBtc = satsAmount / 100_000_000;
            
            var bip21New = network.GenerateBIP21(invoice.Destination, amountInBtc);
            bip21New.QueryParams.Add("label", invoice.User.Name);
            // TODO: Add parameter here on which payroll invoice it is being paid, so that when wallet sends trasaction you can mark it paid
            // bip21New.QueryParams.Add("payrollInvoiceId", invoice.Id);
            bip21.Add(bip21New.ToString());

            invoice.State = PayrollInvoiceState.AwaitingPayment;
        }

        await ctx.SaveChangesAsync();

        var strRates = String.Join(", ", rates.Select(a => $"BTC/{a.Key}:{Math.Ceiling(100/a.Value)/100}"));
        TempData.SetStatusMessageModel(new StatusMessageModel()
        {
            Severity = StatusMessageModel.StatusSeverity.Info,
            Message = $"Payroll on {DateTime.Now:yyyy-MM-dd} for {invoices.Count} invoices. {strRates}"
        });

        return new RedirectToActionResult("WalletSend", "UIWallets",
            new
            {
                walletId = new WalletId(CurrentStore.Id, PayrollPluginConst.BTC_CRYPTOCODE).ToString(),
                bip21
            });
    }

    [HttpGet("~/plugins/{storeId}/payroll/upload")]
    public async Task<IActionResult> Upload(string storeId)
    {
        var settings = await payrollPluginDbContextFactory.GetSettingAsync(storeId);
        var model = new PayrollInvoiceUploadViewModel
        {
            Amount = 0,
            Currency = CurrentStore.GetStoreBlob().DefaultCurrency,
            PurchaseOrdersRequired = settings.PurchaseOrdersRequired
        };

        await using var ctx = payrollPluginDbContextFactory.CreateContext();
        model.PayrollUsers = getPayrollUsers(ctx, CurrentStore.Id);
        if (!model.PayrollUsers.Any())
        {
            return NoUserResult(storeId);
        }
        if (model.PayrollUsers.Any())
        {
            model.UserId = model.PayrollUsers.First().Value;
        }
        return View(model);
    }

    private static SelectList getPayrollUsers(PayrollPluginDbContext ctx, string storeId)
    {
        var payrollUsers = ctx.PayrollUsers.Where(a => a.StoreId == storeId && a.State == PayrollUserState.Active)
            .Select(a => new SelectListItem { Text = $"{a.Name} <{a.Email}>", Value = a.Id })
            .ToList();
        return new SelectList(payrollUsers, nameof(SelectListItem.Value), nameof(SelectListItem.Text));
    }

    [HttpPost("~/plugins/{storeId}/payroll/upload")]

    public async Task<IActionResult> Upload(string storeId, PayrollInvoiceUploadViewModel model)
    {
        if (CurrentStore is null)
            return NotFound();

        if (model.Amount <= 0)
        {
            ModelState.AddModelError(nameof(model.Amount), "Amount must be more than 0.");
        }

        try
        {
            var network = networkProvider.GetNetwork<BTCPayNetwork>(PayrollPluginConst.BTC_CRYPTOCODE);
            var unused = Network.Parse<BitcoinAddress>(model.Destination, network.NBitcoinNetwork);
        }
        catch (Exception)
        {
            ModelState.AddModelError(nameof(model.Destination), "Invalid Destination, check format of address.");
        }

        await using var dbPlugin = payrollPluginDbContextFactory.CreateContext();
        var settings = await dbPlugin.GetSettingAsync(storeId);
        if (!settings.MakeInvoiceFilesOptional && model.Invoice == null)
        {
            ModelState.AddModelError(nameof(model.Invoice), "Kindly include an invoice");
        }
        
        if (settings.PurchaseOrdersRequired && string.IsNullOrEmpty(model.PurchaseOrder))
        {
            model.PurchaseOrdersRequired = true;
            ModelState.AddModelError(nameof(model.PurchaseOrder), "Purchase Order is required");
        }

        var alreadyInvoiceWithAddress = dbPlugin.PayrollInvoices.Any(a =>
            a.Destination == model.Destination &&
            a.State != PayrollInvoiceState.Completed && a.State != PayrollInvoiceState.Cancelled);

        if (alreadyInvoiceWithAddress)
            ModelState.AddModelError(nameof(model.Destination), "This destination is already specified for another invoice from which payment is in progress");

        if (!ModelState.IsValid)
        {
            model.PayrollUsers = getPayrollUsers(dbPlugin, CurrentStore.Id);
            return View(model);
        }

        // TODO: Make saving of the file and entry in the database atomic
        var removeTrailingZeros = model.Amount % 1 == 0 ? (int)model.Amount : model.Amount; // this will remove .00 from the amount
        var dbPayrollInvoice = new PayrollInvoice
        {
            Amount = removeTrailingZeros,
            CreatedAt = DateTime.UtcNow,
            Currency = model.Currency,
            Destination = model.Destination,
            PurchaseOrder = model.PurchaseOrder,
            Description = model.Description,
            UserId = model.UserId,
            State = PayrollInvoiceState.AwaitingApproval
        };
        if (!settings.MakeInvoiceFilesOptional && model.Invoice != null)
        {
            var adminset = await settingsRepository.GetSettingAsync<PayrollPluginSettings>();
            var uploaded = await fileService.AddFile(model.Invoice, adminset!.AdminAppUserId);
            dbPayrollInvoice.InvoiceFilename = uploaded.Id;
        }

        dbPlugin.Add(dbPayrollInvoice);
        await dbPlugin.SaveChangesAsync();

        TempData.SetStatusMessageModel(new StatusMessageModel()
        {
            Message = $"Invoice uploaded successfully",
            Severity = StatusMessageModel.StatusSeverity.Success
        });
        return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
    }

    [HttpGet("~/plugins/payroll/delete/{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        if (CurrentStore is null)
            return NotFound();

        await using var ctx = payrollPluginDbContextFactory.CreateContext();
        PayrollInvoice invoice = ctx.PayrollInvoices.Include(c => c.User)
            .SingleOrDefault(a => a.Id == id);

        if (invoice == null)
            return NotFound();

        if (invoice.State != PayrollInvoiceState.AwaitingApproval)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = $"Invoice cannot be deleted as it has been actioned upon",
                Severity = StatusMessageModel.StatusSeverity.Error
            });
            return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
        }
        return View("Confirm", new ConfirmModel($"Delete Invoice", $"Do you really want to delete the invoice for {invoice.Amount} {invoice.Currency} from {invoice.User.Name}?", "Delete"));
    }

    [HttpPost("~/plugins/payroll/delete/{id}")]
    public async Task<IActionResult> DeletePost(string id)
    {
        if (CurrentStore is null)
            return NotFound();

        await using var ctx = payrollPluginDbContextFactory.CreateContext();

        var invoice = ctx.PayrollInvoices.Single(a => a.Id == id);

        if (invoice.State != PayrollInvoiceState.AwaitingApproval)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = $"Invoice cannot be deleted as it has been actioned upon",
                Severity = StatusMessageModel.StatusSeverity.Error
            });
            return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
        }

        ctx.Remove(invoice);
        await ctx.SaveChangesAsync();

        TempData.SetStatusMessageModel(new StatusMessageModel()
        {
            Message = $"Invoice deleted successfully",
            Severity = StatusMessageModel.StatusSeverity.Success
        });
        return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
    } 
    
    private async Task<IActionResult> ExportInvoices(List<PayrollInvoice> invoices)
    {
        if (CurrentStore is null)
            return NotFound();

        if (!invoices.Any())
        {
            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Message = $"No invoice transaction found",
                Severity = StatusMessageModel.StatusSeverity.Error
            });
            return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
        }

        var transactionIds = invoices.Where(a => a.BtcPaid != null).Select(a => a.TxnId).Distinct().ToList();
        var walletTxnInfo = transactionIds.Any() ? await GetWalletTransactions(transactionIds) : null;
        var fileName = $"PayrollInvoices-{DateTime.Now:yyyy_MM_dd-HH_mm_ss}.csv";

        var csvData = new StringBuilder();
        csvData.AppendLine("Created Date,Transaction Date,Name,InvoiceId,Address,Currency,Amount,Balance,BTCUSD Rate, BTCJPY Rate,Balance,TransactionId,PaidInWallet");
        string emptyStr = string.Empty;
        decimal usdRate = 0;
        foreach (var invoice in invoices)
        {
            if (invoice.BtcPaid == null)
            {
                csvData.AppendLine($"{invoice.CreatedAt:MM/dd/yy HH:mm},{emptyStr},{invoice.User.Name},{invoice.Id}," +
                                   $"{invoice.Destination},{invoice.Currency},{invoice.Amount},{usdRate},{emptyStr}" +
                                   $",{usdRate},{emptyStr},{emptyStr},false");
            }
            else
            {
                var txn = walletTxnInfo?.Transactions.SingleOrDefault(a => a.Id == invoice.TxnId);
                //string balance = string.IsNullOrEmpty(transaction.Balance) ? "" : transaction.Balance;

                var btcPaid = Convert.ToDecimal(invoice.BtcPaid);
                if (btcPaid > 0)
                {
                    usdRate = Math.Floor(Convert.ToDecimal(invoice.Amount) / btcPaid);
                    usdRate = Math.Abs(usdRate);
                }

                csvData.AppendLine($"{invoice.CreatedAt:MM/dd/yy HH:mm},{txn?.Timestamp.ToString("MM/dd/yy HH:mm")},{invoice.User.Name},{emptyStr}" +
                                   $",{invoice.Destination},{invoice.Currency},-{invoice.Amount},{usdRate},{emptyStr}" +
                                   $",-{invoice.BtcPaid},{emptyStr},{invoice.TxnId},true");
            }
        }
        
        byte[] fileBytes = Encoding.UTF8.GetBytes(csvData.ToString());
        return File(fileBytes, "text/csv", fileName);
    }

    private async Task<ListTransactionsViewModel> GetWalletTransactions(List<string> transactionIds)
    {
        var  model = new ListTransactionsViewModel();
        WalletId walletId = new WalletId(CurrentStore.Id, PayrollPluginConst.BTC_CRYPTOCODE);
        var paymentMethod = CurrentStore.GetDerivationSchemeSettings(handlers, walletId.CryptoCode);
        
        var wallet = walletProvider.GetWallet(walletId.CryptoCode);
        var walletTransactionsInfo = await walletRepository.GetWalletTransactionsInfo(
            walletId, transactionIds.ToArray());
        
        // TODO: This will only select first 100 transactions, fix it
        foreach (var transactionId in transactionIds)
        {
            var txnIdUid = uint256.Parse(transactionId);
            
            var tx = await wallet.FetchTransaction(paymentMethod.AccountDerivation, txnIdUid);
            var vm = new ListTransactionsViewModel.TransactionViewModel
            {
                Id = tx.TransactionId.ToString(),
                Timestamp = tx.SeenAt,
                Balance = tx.BalanceChange.ShowMoney(wallet.Network),
                IsConfirmed = tx.Confirmations != 0
            };

            if (walletTransactionsInfo.TryGetValue(tx.TransactionId.ToString(), out var transactionInfo))
            {
                var labels = labelService.CreateTransactionTagModels(transactionInfo, Request);
                vm.Tags.AddRange(labels);
                vm.Comment = transactionInfo.Comment;
            }

            model.Transactions.Add(vm);
        }
        return model;
    }

    private IActionResult NoUserResult(string storeId)
    {
        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Severity = StatusMessageModel.StatusSeverity.Error,
            Html = $"To upload a payroll, you need to create a <a href='{Url.Action(nameof(PayrollUserController.Create), "PayrollUser", new { storeId })}' class='alert-link'>user</a> first",
            AllowDismiss = false
        });
        return RedirectToAction(nameof(List), new { storeId });
    }
}