using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.HostedServices;
using BTCPayServer.Migrations;
using BTCPayServer.ModelBinders;
using BTCPayServer.Models;
using BTCPayServer.Models.WalletViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.PointOfSale.Models;
using BTCPayServer.Rating;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data.Models;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBXplorer;
using NLog.Layouts;
using static BTCPayServer.RockstarDev.Plugins.Payroll.PayrollUserController;

namespace BTCPayServer.RockstarDev.Plugins.Payroll;

[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class PayrollInvoiceController : Controller
{
    private readonly PullPaymentHostedService _pullPaymentHostedService;
    private readonly UIStorePullPaymentsController _uiStorePullPaymentsController;
    private readonly ApplicationDbContextFactory _dbContextFactory;
    private readonly PayrollPluginDbContextFactory _payrollPluginDbContextFactory;
    private readonly IEnumerable<IPayoutHandler> _payoutHandlers;
    private readonly StoreRepository _storeRepository;
    private readonly RateFetcher _rateFetcher;
    private readonly BTCPayNetworkProvider _networkProvider;
    private readonly AppService _appService;
    private readonly IFileService _fileService;
    private readonly UserManager<ApplicationUser> _userManager;

    public PayrollInvoiceController(PullPaymentHostedService pullPaymentHostedService,
        UIStorePullPaymentsController uiStorePullPaymentsController,
        ApplicationDbContextFactory dbContextFactory,
        PayrollPluginDbContextFactory payrollPluginDbContextFactory,
        IEnumerable<IPayoutHandler> payoutHandlers, StoreRepository storeRepository, RateFetcher rateFetcher, BTCPayNetworkProvider networkProvider,
            AppService appService,
        IFileService fileService,

            UserManager<ApplicationUser> userManager)
    {
        _pullPaymentHostedService = pullPaymentHostedService;
        _uiStorePullPaymentsController = uiStorePullPaymentsController;
        _dbContextFactory = dbContextFactory;
        _payrollPluginDbContextFactory = payrollPluginDbContextFactory;
        _payoutHandlers = payoutHandlers;
        _storeRepository = storeRepository;
        _rateFetcher = rateFetcher;
        _networkProvider = networkProvider;
        _appService = appService;
        _fileService = fileService;
        this._userManager = userManager;
    }
    public StoreData CurrentStore => HttpContext.GetStoreData();

    // TODO: We need robust way to fetch this User Id, to which files will be bound
    public string GetAdminUserId() => _userManager.GetUserId(User);

    private const string CURRENCY = "USD";


    [HttpGet("~/plugins/{storeId}/payroll/list")]
    public async Task<IActionResult> List(string storeId)
    {
        var now = DateTimeOffset.UtcNow;
        await using var ctx = _payrollPluginDbContextFactory.CreateContext();
        var payrollInvoices = await ctx.PayrollInvoices
            .Include(data => data.User)
            .Where(p => p.User.StoreId == storeId && p.IsArchived == false)
            .OrderByDescending(data => data.CreatedAt).ToListAsync();

        return View(payrollInvoices.Select(tuple => new PayrollInvoiceViewModel()
        {
            CreatedAt = tuple.CreatedAt,
            Id = tuple.Id,
            Name = tuple.User.Name,
            Email = tuple.User.Email,
            Destination = tuple.Destination,
            Amount = tuple.Amount,
            Currency = tuple.Currency,
            State = tuple.State,
            Description = tuple.Description,
            InvoiceUrl = tuple.InvoiceFilename
        }).ToList()
        );
    }
    public class PayrollInvoiceViewModel
    {
        // TODO: Implement selection and generation of invoices to pay through Bitcoin wallet
        // public bool Selected { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public string Id { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string Destination { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; }
        public PayrollInvoiceState State { get; set; }
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

        switch (command)
        {
            case "payinvoices":
                return await payInvoices(selectedItems);

            case "markpaid":
                using (var ctx = _payrollPluginDbContextFactory.CreateContext())
                {
                    var invoices = ctx.PayrollInvoices
                        .Include(a => a.User)
                        .Where(a => selectedItems.Contains(a.Id))
                        .ToList();

                    foreach (var invoice in invoices)
                    {
                        invoice.State = PayrollInvoiceState.Completed;
                    }

                    ctx.SaveChanges();
                }
                break;

            default:
                break;
        }
        return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
    }

    // TODO: Is there a better way here to make it more generic?
    private const string BTC_CRYPTOCODE = "BTC";
    private async Task<IActionResult> payInvoices(string[] selectedItems)
    {
        await using var ctx = _payrollPluginDbContextFactory.CreateContext();
        var invoices = ctx.PayrollInvoices
            .Include(a => a.User)
            .Where(a => selectedItems.Contains(a.Id))
            .ToList();

        var network = _networkProvider.GetNetwork<BTCPayNetwork>(BTC_CRYPTOCODE);
        var bip21 = new List<string>();
        foreach (var invoice in invoices)
        {
            var amountInBtc = await usdToBtcAmount(invoice);
            var bip21New = network.GenerateBIP21(invoice.Destination, amountInBtc);
            bip21New.QueryParams.Add("label", invoice.User.Name);
            // TODO: Add parameter here on which payroll invoice it is being paid, so that when wallet sends trasaction you can mark it paid
            // bip21New.QueryParams.Add("payrollInvoiceId", invoice.Id);
            bip21.Add(bip21New.ToString());

            invoice.State = PayrollInvoiceState.AwaitingPayment;
        }

        await ctx.SaveChangesAsync();

        return new RedirectToActionResult("WalletSend", "UIWallets", new { walletId = new WalletId(CurrentStore.Id, BTC_CRYPTOCODE).ToString(), bip21 });
    }

    private async Task<decimal> usdToBtcAmount(PayrollInvoice invoice)
    {
        if (invoice.Currency == BTC_CRYPTOCODE)
            return invoice.Amount;

        var rate = await _rateFetcher.FetchRate(new CurrencyPair(invoice.Currency, BTC_CRYPTOCODE),
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
    public async Task<IActionResult> Upload()
    {
        var model = new PayrollInvoiceUploadViewModel();
        model.Currency = CurrentStore.GetStoreBlob().DefaultCurrency;

        await using var ctx = _payrollPluginDbContextFactory.CreateContext();
        model.PayrollUsers = getPayrollUsers(ctx, CurrentStore.Id);
        if (model.PayrollUsers.Any())
        {
            model.UserId = model.PayrollUsers.First().Value;
        }
        return View(model);
    }

    private static SelectList getPayrollUsers(PayrollPluginDbContext ctx, string storeId)
    {
        var payrollUsers = ctx.PayrollUsers.Where(a => a.StoreId == storeId)
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
            var network = _networkProvider.GetNetwork<BTCPayNetwork>(BTC_CRYPTOCODE);
            var address = Network.Parse<BitcoinAddress>(model.Destination, network.NBitcoinNetwork);
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(nameof(model.Destination), "Invalid Destination, check format of address.");
        }

        await using var ctx = _payrollPluginDbContextFactory.CreateContext();

        if (!ModelState.IsValid)
        {
            model.PayrollUsers = getPayrollUsers(ctx, CurrentStore.Id);
            return View(model);
        }

        // TODO: Make saving of the file and entry in the database atomic
        // TODO: Figure out abstraction of GetAdminUserId()
        var uploaded = await _fileService.AddFile(model.Invoice, GetAdminUserId());

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

        ctx.Add(dbPayrollInvoice);
        await ctx.SaveChangesAsync();

        this.TempData.SetStatusMessageModel(new StatusMessageModel()
        {
            Message = $"Invoice uploaded successfully",
            Severity = StatusMessageModel.StatusSeverity.Success
        });
        return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
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
        [Required]
        public IFormFile Invoice { get; set; }



        public SelectList PayrollUsers { get; set; }
    }
}