using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Models.WalletViewModels;
using BTCPayServer.Rating;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data.Models;
using BTCPayServer.RockstarDev.Plugins.Payroll.Services;
using BTCPayServer.RockstarDev.Plugins.Payroll.Services.Helpers;
using BTCPayServer.RockstarDev.Plugins.Payroll.ViewModels;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Labels;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using NBitcoin;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Controllers;

[Route("~/plugins/{storeId}/vendorpay/", Order = 0)]
[Route("~/plugins/{storeId}/payroll/", Order = 1)]
[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class PayrollInvoiceController(
    PluginDbContextFactory pluginDbContextFactory,
    DefaultRulesCollection defaultRulesCollection,
    RateFetcher rateFetcher,
    PaymentMethodHandlerDictionary handlers,
    BTCPayNetworkProvider networkProvider,
    UserManager<ApplicationUser> userManager,
    ISettingsRepository settingsRepository,
    BTCPayWalletProvider walletProvider,
    WalletRepository walletRepository,
    LabelService labelService,
    EmailService emailService,
    PayrollInvoiceUploadHelper payrollInvoiceUploadHelper,
    InvoicesDownloadHelper invoicesDownloadHelper)
    : Controller
{
    private StoreData CurrentStore => HttpContext.GetStoreData();

    [HttpGet("list")]
    public async Task<IActionResult> List(string storeId, bool all)
    {
        await using var ctx = pluginDbContextFactory.CreateContext();
        var payrollInvoices = await ctx.PayrollInvoices
            .Include(data => data.User)
            .Where(p => p.User.StoreId == storeId && !p.IsArchived)
            .OrderByDescending(data => data.CreatedAt).ToListAsync();

        if (!all) payrollInvoices = payrollInvoices.Where(a => a.User.State == PayrollUserState.Active).ToList();

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
                ExtraInvoiceFiles = tuple.ExtraFilenames,
                Description = tuple.Description,
                InvoiceUrl = tuple.InvoiceFilename,
                PaidAt = tuple.PaidAt,
                AdminNote = tuple.AdminNote
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

        var ctx = pluginDbContextFactory.CreateContext();
        var invoices = ctx.PayrollInvoices
            .Include(a => a.User)
            .Where(a => selectedItems.Contains(a.Id))
            .ToList();

        switch (command)
        {
            case "emailconfirmation":
                await emailService.SendSuccessfulInvoicePaymentEmail(invoices);
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Message = "Email Notifications executed on selected invoices per existing settings",
                    Severity = StatusMessageModel.StatusSeverity.Success
                });
                break;

            case "payinvoices":
                return await payInvoices(selectedItems);

            case "markpaid":
                invoices.ForEach(c =>
                {
                    c.State = PayrollInvoiceState.Completed;
                    c.PaidAt = DateTimeOffset.UtcNow;
                });
                await ctx.SaveChangesAsync();

                // Mark Paid doesn't trigger "paid" email sending, it's something we can add in future versions
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Message = "Invoices successfully marked as paid", Severity = StatusMessageModel.StatusSeverity.Success
                });
                break;

            case "download":
                var invoicesWithFile = invoices.Where(i => !string.IsNullOrWhiteSpace(i.InvoiceFilename)).ToList();
                if (!invoicesWithFile.Any())
                {
                    TempData.SetStatusMessageModel(new StatusMessageModel
                    {
                        Message = "No invoice file available to download", Severity = StatusMessageModel.StatusSeverity.Info
                    });
                    return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
                }

                return await invoicesDownloadHelper.Process(invoicesWithFile, HttpContext.Request.GetAbsoluteRootUri());

            case "export":
                return await ExportInvoices(invoices);
        }

        return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
    }

    public async Task<IActionResult> DownloadInvoices(string invoiceId)
    {
        if (CurrentStore is null)
            return NotFound();

        await using var ctx = pluginDbContextFactory.CreateContext();
        var invoice = ctx.PayrollInvoices
            .Include(a => a.User)
            .Single(a => a.Id == invoiceId && a.User.StoreId == CurrentStore.Id);
        return await invoicesDownloadHelper.Process([invoice], HttpContext.Request.GetAbsoluteRootUri());
    }


    private async Task<IActionResult> payInvoices(string[] selectedItems)
    {
        await using var ctx = pluginDbContextFactory.CreateContext();
        var invoices = ctx.PayrollInvoices
            .Include(a => a.User)
            .Where(a => selectedItems.Contains(a.Id))
            .ToList();

        // initialize exchange rates
        var rates = new Dictionary<string, decimal>();
        var currencies = invoices.Select(a => a.Currency).Distinct().ToList();
        foreach (var currency in currencies)
            if (currency == PayrollPluginConst.BTC_CRYPTOCODE)
            {
                rates.Add(currency, 1);
            }
            else
            {
                var rate = await rateFetcher.FetchRate(new CurrencyPair(currency, PayrollPluginConst.BTC_CRYPTOCODE),
                    CurrentStore.GetStoreBlob().GetRateRules(defaultRulesCollection), new StoreIdRateContext(CurrentStore.Id), CancellationToken.None);
                if (rate.BidAsk == null) throw new Exception("Currency is not supported");

                rates.Add(currency, rate.BidAsk.Bid);
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

        var strRates = string.Join(", ", rates.Select(a => $"BTC/{a.Key}:{Math.Ceiling(100 / a.Value) / 100}"));
        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Severity = StatusMessageModel.StatusSeverity.Info, Message = $"Payroll on {DateTime.Now:yyyy-MM-dd} for {invoices.Count} invoices. {strRates}"
        });

        return new RedirectToActionResult("WalletSend", "UIWallets",
            new { walletId = new WalletId(CurrentStore.Id, PayrollPluginConst.BTC_CRYPTOCODE).ToString(), bip21 });
    }

    [HttpGet("upload")]
    public async Task<IActionResult> Upload(string storeId)
    {
        var settings = await pluginDbContextFactory.GetSettingAsync(storeId);
        var model = new PayrollInvoiceUploadViewModel
        {
            Amount = 0,
            Currency = CurrentStore.GetStoreBlob().DefaultCurrency,
            PurchaseOrdersRequired = settings.PurchaseOrdersRequired
        };

        await using var ctx = pluginDbContextFactory.CreateContext();
        model.PayrollUsers = getPayrollUsers(ctx, CurrentStore.Id);
        if (!model.PayrollUsers.Any()) return NoUserResult(storeId);
        if (model.PayrollUsers.Any()) model.UserId = model.PayrollUsers.First().Value;
        return View(model);
    }

    private static SelectList getPayrollUsers(PluginDbContext ctx, string storeId)
    {
        var payrollUsers = ctx.PayrollUsers.Where(a => a.StoreId == storeId && a.State == PayrollUserState.Active)
            .Select(a => new SelectListItem { Text = $"{a.Name} <{a.Email}>", Value = a.Id })
            .ToList();
        return new SelectList(payrollUsers, nameof(SelectListItem.Value), nameof(SelectListItem.Text));
    }


    [HttpPost("upload")]
    public async Task<IActionResult> Upload(string storeId, PayrollInvoiceUploadViewModel model)
    {
        if (CurrentStore is null)
            return NotFound();

        var validation = await payrollInvoiceUploadHelper.Process(storeId, model.UserId, model);
        if (!validation.IsValid)
        {
            await using var ctx = pluginDbContextFactory.CreateContext();
            model.PayrollUsers = getPayrollUsers(ctx, CurrentStore.Id);
            validation.ApplyToModelState(ModelState);
            return View(model);
        }

        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Message = "Invoice uploaded successfully", Severity = StatusMessageModel.StatusSeverity.Success
        });

        return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
    }

    [HttpGet("delete/{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        if (CurrentStore is null)
            return NotFound();

        await using var ctx = pluginDbContextFactory.CreateContext();

        var invoice = ctx.PayrollInvoices.Include(c => c.User)
            .SingleOrDefault(a => a.Id == id);

        if (invoice == null)
            return NotFound();

        if (invoice.State != PayrollInvoiceState.AwaitingApproval)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = "Invoice cannot be deleted as it has been actioned upon", Severity = StatusMessageModel.StatusSeverity.Error
            });
            return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
        }

        return View("Confirm",
            new ConfirmModel("Delete Invoice", $"Do you really want to delete the invoice for {invoice.Amount} {invoice.Currency} from {invoice.User.Name}?",
                "Delete"));
    }

    [HttpPost("delete/{id}")]
    public async Task<IActionResult> DeletePost(string id)
    {
        if (CurrentStore is null)
            return NotFound();

        await using var ctx = pluginDbContextFactory.CreateContext();

        var invoice = ctx.PayrollInvoices.Single(a => a.Id == id);

        if (invoice.State != PayrollInvoiceState.AwaitingApproval)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = "Invoice cannot be deleted as it has been actioned upon", Severity = StatusMessageModel.StatusSeverity.Error
            });
            return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
        }

        ctx.Remove(invoice);
        await ctx.SaveChangesAsync();

        TempData.SetStatusMessageModel(
            new StatusMessageModel { Message = "Invoice deleted successfully", Severity = StatusMessageModel.StatusSeverity.Success });
        return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
    }

    private async Task<IActionResult> ExportInvoices(List<PayrollInvoice> invoices)
    {
        if (CurrentStore is null)
            return NotFound();

        if (!invoices.Any())
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = "No invoice transaction found", Severity = StatusMessageModel.StatusSeverity.Error
            });
            return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
        }
        var transactionIds = invoices.Where(a => a.BtcPaid != null).Select(a => a.TxnId).Distinct().ToList();
        var walletTxnInfo = transactionIds.Any() ? await GetWalletTransactions(transactionIds) : null;
        var fileName = $"PayrollInvoices-{DateTime.Now:yyyy_MM_dd-HH_mm_ss}.csv";

        var csvData = new StringBuilder();
        // We preserve this format with duplicate fields because Emperor Nicolas Dorier uses it, maybe in future we add compatibility mode
        csvData.AppendLine(
            "Created Date,Transaction Date,Name,InvoiceDesc,"+
            "Address,Currency,Amount,"+
            "BTC-Currency Rate,Amount in BTC,TransactionId," +
            "PaidInWallet");
        var empty = string.Empty;
        decimal currencyRate = 0;
        foreach (var invoice in invoices)
        {
            var desc = $"{invoice.Description ?? empty}";
            if (!string.IsNullOrEmpty(invoice.PurchaseOrder))
                desc = $"{invoice.PurchaseOrder} - {desc}";

            var formattedDesc = "\"" + desc.Replace("\"", "\"\"") + "\"";

            if (invoice.BtcPaid == null)
            {
                csvData.AppendLine(
                    $"{invoice.CreatedAt:MM/dd/yy HH:mm},{invoice.PaidAt?.ToString("MM/dd/yy HH:mm") ?? empty},{invoice.User.Name},{formattedDesc}," +
                    $"{invoice.Destination},{invoice.Currency},-{invoice.Amount},"+
                    $"{currencyRate},{empty},{empty},"+
                    $"false");
            }
            else
            {
                var txn = walletTxnInfo?.Transactions?.SingleOrDefault(a => a.Id == invoice.TxnId);
                //string balance = string.IsNullOrEmpty(transaction.Balance) ? "" : transaction.Balance;

                var btcPaid = Convert.ToDecimal(invoice.BtcPaid);
                if (btcPaid > 0)
                {
                    currencyRate = Math.Floor(Convert.ToDecimal(invoice.Amount) / btcPaid);
                    currencyRate = Math.Abs(currencyRate);
                }

                csvData.AppendLine(
                    $"{invoice.CreatedAt:MM/dd/yy HH:mm},{invoice.PaidAt?.ToString("MM/dd/yy HH:mm" ?? empty)},{invoice.User.Name},{formattedDesc}," +
                    $"{invoice.Destination},{invoice.Currency},-{invoice.Amount},"+
                    $"{currencyRate},-{invoice.BtcPaid},{invoice.TxnId},"+
                    $"true");
            }
        }

        var fileBytes = Encoding.UTF8.GetBytes(csvData.ToString());
        return File(fileBytes, "text/csv", fileName);
    }

    private async Task<ListTransactionsViewModel> GetWalletTransactions(List<string> transactionIds)
    {
        var model = new ListTransactionsViewModel();
        var walletId = new WalletId(CurrentStore.Id, PayrollPluginConst.BTC_CRYPTOCODE);
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
            Html =
                $"To upload a payroll, you need to create a <a href='{Url.Action(nameof(PayrollUserController.Create), "PayrollUser", new { storeId })}' class='alert-link'>user</a> first",
            AllowDismiss = false
        });
        return RedirectToAction(nameof(List), new { storeId });
    }

    [HttpGet("adminnote/{id}")]
    public async Task<IActionResult> AdminNote(string id)
    {
        if (CurrentStore is null)
            return NotFound();

        await using var ctx = pluginDbContextFactory.CreateContext();
        var invoice = ctx.PayrollInvoices.Include(c => c.User)
            .SingleOrDefault(a => a.Id == id);

        if (invoice == null)
            return NotFound();

        var model = new AdminNoteViewModel { Id = invoice.Id, AdminNote = invoice.AdminNote };

        return View(model);
    }

    [HttpPost("adminnote/{id}")]
    public async Task<IActionResult> AdminNote(AdminNoteViewModel model)
    {
        if (CurrentStore is null)
            return NotFound();

        await using var ctx = pluginDbContextFactory.CreateContext();

        var invoice = ctx.PayrollInvoices.Single(a => a.Id == model.Id);
        invoice.AdminNote = model.AdminNote;

        await ctx.SaveChangesAsync();

        return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
    }
}
