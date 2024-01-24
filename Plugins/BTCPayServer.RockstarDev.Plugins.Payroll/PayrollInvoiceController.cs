using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
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
            Id = tuple.Id,
            Name = tuple.User.Name,
            Email = tuple.User.Email,
            Amount = tuple.Amount,
            Currency = tuple.Currency,
            Description = tuple.Description,
            InvoiceUrl = tuple.InvoiceFilename
        }).ToList()
        );
    }
    public class PayrollInvoiceViewModel
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; }
        public string Description { get; set; }
        public string InvoiceUrl { get; set; }
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

        await using var ctx = _payrollPluginDbContextFactory.CreateContext();

        if (!ModelState.IsValid)
        {
            model.PayrollUsers = getPayrollUsers(ctx, CurrentStore.Id);
            return View(model);
        }

        // TODO: Make save of the file and entry in the database atomic
        var uploaded = await _fileService.AddFile(model.Invoice, GetAdminUserId());
        Debug.WriteLine("File uploaded to " + uploaded.StorageFileName);

        var dbPayrollInvoice = new PayrollInvoice
        {
            Amount = model.Amount,
            CreatedAt = DateTime.UtcNow,
            Currency = model.Currency,
            Description = model.Description,
            InvoiceFilename = uploaded.StorageFileName,
            UserId = model.UserId
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
        public decimal Amount { get; set; }
        [Required]
        public string Currency { get; set; }
        public string Description { get; set; }
        [Required]
        public IFormFile Invoice { get; set; }



        public SelectList PayrollUsers { get; set; }
    }
}