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
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Services;
using BTCPayServer.Services.Wallets;
using BTCPayServer.Models.WalletViewModels;
using BTCPayServer.Services.Labels;
using BTCPayServer.RockstarDev.Plugins.Payroll.Helper;
using System.Text;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Controllers;

[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class PayrollInvoiceController : Controller
{
    private WalletRepository WalletRepository { get; }

    private readonly ApplicationDbContextFactory _dbContextFactory;
    private readonly PayrollPluginDbContextFactory _payrollPluginDbContextFactory;
    private readonly RateFetcher _rateFetcher;
    private readonly BTCPayNetworkProvider _networkProvider;
    private readonly IFileService _fileService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISettingsRepository _settingsRepository;
    private readonly HttpClient _httpClient;
    private readonly BTCPayWalletProvider _walletProvider;
    private readonly LabelService _labelService;

    public PayrollInvoiceController(ApplicationDbContextFactory dbContextFactory,
        PayrollPluginDbContextFactory payrollPluginDbContextFactory,
        RateFetcher rateFetcher,
        BTCPayNetworkProvider networkProvider,
        IFileService fileService,
        UserManager<ApplicationUser> userManager,
        ISettingsRepository settingsRepository,
        HttpClient httpClient,
        BTCPayWalletProvider walletProvider,
        WalletRepository walletRepository,
        LabelService labelService)
    {
        _labelService = labelService;
        _walletProvider = walletProvider;
        WalletRepository = walletRepository;
        _dbContextFactory = dbContextFactory;
        _payrollPluginDbContextFactory = payrollPluginDbContextFactory;
        _rateFetcher = rateFetcher;
        _networkProvider = networkProvider;
        _fileService = fileService;
        _userManager = userManager;
        _settingsRepository = settingsRepository;
        _httpClient = httpClient;
    }
    public StoreData CurrentStore => HttpContext.GetStoreData();

    [HttpGet("~/plugins/{storeId}/payroll/list")]
    public async Task<IActionResult> List(string storeId, bool all)
    {
        await using var ctx = _payrollPluginDbContextFactory.CreateContext();
        var payrollInvoices = await ctx.PayrollInvoices
            .Include(data => data.User)
            .Where(p => p.User.StoreId == storeId && !p.IsArchived)
            .OrderByDescending(data => data.CreatedAt).ToListAsync();

        List<PayrollInvoice> pendingPayrollInvoices = payrollInvoices.Where(c => c.State == PayrollInvoiceState.AwaitingApproval).ToList();
        if (pendingPayrollInvoices.Any())
        {
            await ValidatePaidInvoice(pendingPayrollInvoices, ctx);
        }

        if (!all)
        {
            payrollInvoices = payrollInvoices.Where(a => a.User.State == PayrollUserState.Active).ToList();
        }

        // triggering saving of admin user id if needed
        var settings = await _settingsRepository.GetSettingAsync<PayrollPluginSettings>();
        settings ??= new PayrollPluginSettings();
        if (settings.AdminAppUserId is null)
        {
            settings.AdminAppUserId = _userManager.GetUserId(User);
            await _settingsRepository.UpdateSetting(settings);
        }

        PayrollInvoiceListViewModel model = new PayrollInvoiceListViewModel
        {
            All = all,
            PayrollInvoices = payrollInvoices.Select(tuple => new PayrollInvoiceViewModel()
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
                Description = tuple.Description,
                InvoiceUrl = tuple.InvoiceFilename
            }).ToList()
        };
        return View(model);
    }

    public class PayrollInvoiceListViewModel
    {
        public bool All { get; set; }
        public List<PayrollInvoiceViewModel> PayrollInvoices { get; set; }
    }

    public class PayrollInvoiceViewModel
    {
        public DateTimeOffset CreatedAt { get; set; }
        public string Id { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string Destination { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; }
        public PayrollInvoiceState State { get; set; }
        public string TxnId { get; set; }
        public string Description { get; set; }
        public string InvoiceUrl { get; set; }
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

        var ctx = _payrollPluginDbContextFactory.CreateContext();
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

            default:
                break;
        }
        return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
    }

    async Task<IActionResult> DownloadInvoicesAsZipAsync(List<PayrollInvoice> invoices)
    {
        var zipName = $"Invoices-{DateTime.Now.ToString("yyyy_MM_dd-HH_mm_ss")}.zip";

        using (MemoryStream ms = new MemoryStream())
        {
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                foreach (var invoice in invoices)
                {
                    var fileUrl = await _fileService.GetFileUrl(HttpContext.Request.GetAbsoluteRootUri(), invoice.InvoiceFilename);
                    var fileBytes = await _httpClient.DownloadFileAsByteArray(fileUrl);
                    string filename = Path.GetFileName(fileUrl);
                    string extension = Path.GetExtension(filename);
                    var entry = zip.CreateEntry($"{filename}{extension}");
                    using (var entryStream = entry.Open())
                    {
                        await entryStream.WriteAsync(fileBytes, 0, fileBytes.Length);
                    }
                }
            }

            return File(ms.ToArray(), "application/zip", zipName);
        }
    }

    private async Task<IActionResult> payInvoices(string[] selectedItems)
    {
        PayrollInvoicePaymentHelper payrollInvoiceHelper = new PayrollInvoicePaymentHelper();
        if (CurrentStore is null)
        {
            return NotFound();
        }
        await using var ctx = _payrollPluginDbContextFactory.CreateContext();
        var invoices = ctx.PayrollInvoices
            .Include(a => a.User)
            .Where(a => selectedItems.Contains(a.Id))
            .ToList();

        var network = _networkProvider.GetNetwork<BTCPayNetwork>(PayrollPluginConst.BTC_CRYPTOCODE);
        var bip21 = new List<string>();
        foreach (var invoice in invoices)
        {
            var amountInBtc = await usdToBtcAmount(invoice);
            var bip21New = network.GenerateBIP21(invoice.Destination, amountInBtc);
            bip21New.QueryParams.Add("label", invoice.Id);
            bip21.Add(bip21New.ToString());
            payrollInvoiceHelper.AddPayrollTransaction(invoice, ctx);
        }
        await ctx.SaveChangesAsync();

        TempData.SetStatusMessageModel(new StatusMessageModel()
        {
            Severity = StatusMessageModel.StatusSeverity.Info,
            Message = $"Payroll on {DateTime.Now:yyyy-MM-dd} for {invoices.Count} invoices"
        });

        return new RedirectToActionResult("WalletSend", "UIWallets",
            new
            {
                walletId = new WalletId(CurrentStore.Id, PayrollPluginConst.BTC_CRYPTOCODE).ToString(),
                bip21
            });
    }

    private async Task ValidatePaidInvoice(List<PayrollInvoice> invoices, PayrollPluginDbContext context)
    {
        PayrollInvoicePaymentHelper payrollInvoiceHelper = new PayrollInvoicePaymentHelper();
        ListTransactionsViewModel txns = await GetWalletTransaction();
        foreach (var invoice in invoices)
        {
            var walletTransaction = txns?.Transactions.Find(t => t.Tags.Any(tag => tag.Text.Contains(invoice.Id)));
            if (walletTransaction != null)
            {
                payrollInvoiceHelper.FinalizePayrollTransaction(invoice, walletTransaction, context);
                invoice.State = PayrollInvoiceState.AwaitingPayment;
                context.PayrollInvoices.Update(invoice);
            }
        }
        await context.SaveChangesAsync();
    }

    private async Task<ListTransactionsViewModel> GetWalletTransaction()
    {

        ListTransactionsViewModel model = new ListTransactionsViewModel();
        WalletId walletId = new WalletId(CurrentStore.Id, PayrollPluginConst.BTC_CRYPTOCODE);
        var paymentMethod = GetDerivationSchemeSettings(walletId);

        var wallet = _walletProvider.GetWallet(paymentMethod.Network);
        var transactions = await wallet.FetchTransactionHistory(paymentMethod.AccountDerivation);
        var walletTransactionsInfo = await WalletRepository.GetWalletTransactionsInfo(walletId, transactions.Select(t => t.TransactionId.ToString()).ToArray());

        foreach (var tx in transactions)
        {
            var vm = new ListTransactionsViewModel.TransactionViewModel();
            vm.Id = tx.TransactionId.ToString();
            vm.Timestamp = tx.SeenAt;
            vm.Balance = tx.BalanceChange.ShowMoney(wallet.Network);
            vm.IsConfirmed = tx.Confirmations != 0;

            if (walletTransactionsInfo.TryGetValue(tx.TransactionId.ToString(), out var transactionInfo))
            {
                var labels = _labelService.CreateTransactionTagModels(transactionInfo, Request);
                vm.Tags.AddRange(labels);
                vm.Comment = transactionInfo.Comment;
            }

            model.Transactions.Add(vm);
        }
        return model;
    }

    private StoreData GetCurrentStore() => HttpContext.GetStoreData();

    internal DerivationSchemeSettings? GetDerivationSchemeSettings(WalletId walletId)
    {
        return GetCurrentStore().GetDerivationSchemeSettings(_networkProvider, walletId.CryptoCode);
    }

    private async Task<decimal> usdToBtcAmount(PayrollInvoice invoice)
    {
        if (invoice.Currency == PayrollPluginConst.BTC_CRYPTOCODE)
            return invoice.Amount;

        var rate = await _rateFetcher.FetchRate(new CurrencyPair(invoice.Currency, PayrollPluginConst.BTC_CRYPTOCODE),
            CurrentStore.GetStoreBlob().GetRateRules(_networkProvider), CancellationToken.None);
        if (rate.BidAsk == null)
        {
            throw new Exception("Currency is not supported");
        }

        var satsAmount = Math.Floor(invoice.Amount * rate.BidAsk.Bid * 100_000_000);
        var amountInBtc = satsAmount / 100_000_000;
        return amountInBtc;
    }

    [HttpGet("~/plugins/{storeId}/payroll/upload")]
    public async Task<IActionResult> Upload(string storeId)
    {
        var model = new PayrollInvoiceUploadViewModel();
        model.Currency = CurrentStore.GetStoreBlob().DefaultCurrency;

        await using var ctx = _payrollPluginDbContextFactory.CreateContext();
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

    public async Task<IActionResult> Upload(PayrollInvoiceUploadViewModel model)
    {
        if (CurrentStore is null)
            return NotFound();

        if (model.Amount <= 0)
        {
            ModelState.AddModelError(nameof(model.Amount), "Amount must be more than 0.");
        }

        try
        {
            var network = _networkProvider.GetNetwork<BTCPayNetwork>(PayrollPluginConst.BTC_CRYPTOCODE);
            var address = Network.Parse<BitcoinAddress>(model.Destination, network.NBitcoinNetwork);
        }
        catch (Exception)
        {
            ModelState.AddModelError(nameof(model.Destination), "Invalid Destination, check format of address.");
        }

        await using var dbPlugin = _payrollPluginDbContextFactory.CreateContext();
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
        var settings = await _settingsRepository.GetSettingAsync<PayrollPluginSettings>();
        var uploaded = await _fileService.AddFile(model.Invoice, settings!.AdminAppUserId);

        var dbPayrollInvoice = new PayrollInvoice
        {
            Amount = model.Amount,
            CreatedAt = DateTime.UtcNow,
            Currency = model.Currency,
            Destination = model.Destination,
            Description = model.Description,
            InvoiceFilename = uploaded.Id,
            UserId = model.UserId,
            State = PayrollInvoiceState.AwaitingApproval
        };

        if (!string.IsNullOrEmpty(model.InvoiceId))
        {
            bool existingInvoice = dbPlugin.PayrollInvoices.Any(c => c.Id == model.InvoiceId);
            if (existingInvoice)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel()
                {
                    Message = $"This invoice Id already exists. Please input a unique invoice Id or allow the system generates one for you",
                    Severity = StatusMessageModel.StatusSeverity.Error
                });
                return RedirectToAction(nameof(Upload), new { storeId = CurrentStore.Id });
            }
            dbPayrollInvoice.Id = model.InvoiceId;
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

    [HttpGet("~/plugins/{storeId}/payroll/invoices/export")]
    public async Task<IActionResult> ExportInvoices(string storeId)
    {
        if (CurrentStore is null)
            return NotFound();

        await using var ctx = _payrollPluginDbContextFactory.CreateContext();
        List<PayrollTransaction> payrollTransactions = await ctx.PayrollTransactions.ToListAsync();

        if (!payrollTransactions.Any())
        {
            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Message = $"No invoice transaction found",
                Severity = StatusMessageModel.StatusSeverity.Error
            });
            return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
        }

        string fileName = $"PayrollInvoices-{DateTime.Now:yyyy_MM_dd-HH_mm_ss}.csv";

        StringBuilder csvData = new StringBuilder();
        csvData.AppendLine("Date,Name,InvoiceId,Address,Currency,Amount,Balance,TransactionId");
        foreach (var transaction in payrollTransactions)
        {
            //string balance = string.IsNullOrEmpty(transaction.Balance) ? "" : transaction.Balance;
            string balance = string.Empty;
            csvData.AppendLine($"{transaction.TransactionDate.ToString("MM/dd/yy HH:mm")},{transaction.Recipient},{transaction.InvoiceId}" +
                $",{transaction.Address},{transaction.Currency},-{transaction.Amount},{balance},{transaction.TransactionId}");
        }
        byte[] fileBytes = Encoding.UTF8.GetBytes(csvData.ToString());

        return File(fileBytes, "text/csv", fileName);
    }

    [HttpGet("~/plugins/payroll/delete/{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        if (CurrentStore is null)
            return NotFound();

        await using var ctx = _payrollPluginDbContextFactory.CreateContext();
        PayrollInvoice invoice = ctx.PayrollInvoices.Include(c => c.User)
            .SingleOrDefault(a => a.Id == id);

        if (invoice == null)
            return NotFound();

        if (invoice.State != PayrollInvoiceState.AwaitingApproval)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel()
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

        await using var ctx = _payrollPluginDbContextFactory.CreateContext();

        PayrollInvoice invoice = ctx.PayrollInvoices.SingleOrDefault(a => a.Id == id);

        if (invoice.State != PayrollInvoiceState.AwaitingApproval)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel()
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

    public class PayrollInvoiceUploadViewModel
    {
        [Required]
        [DisplayName("User")]
        public string UserId { get; set; }
        [Required]
        public string Destination { get; set; }
        [Required]
        public decimal Amount { get; set; }
        [Required]
        public string Currency { get; set; }
        public string Description { get; set; }
        public string InvoiceId { get; set; }
        [Required]
        public IFormFile Invoice { get; set; }



        public SelectList PayrollUsers { get; set; }
    }
}