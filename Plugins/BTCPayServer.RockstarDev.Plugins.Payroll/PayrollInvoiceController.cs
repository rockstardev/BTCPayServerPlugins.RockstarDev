﻿using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Rating;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data.Models;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static BTCPayServer.Controllers.UIBoltcardController;

namespace BTCPayServer.RockstarDev.Plugins.Payroll;

[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class PayrollInvoiceController : Controller
{
    private readonly ApplicationDbContextFactory _dbContextFactory;
    private readonly PayrollPluginDbContextFactory _payrollPluginDbContextFactory;
    private readonly RateFetcher _rateFetcher;
    private readonly BTCPayNetworkProvider _networkProvider;
    private readonly IFileService _fileService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISettingsRepository _settingsRepository;

    public PayrollInvoiceController(ApplicationDbContextFactory dbContextFactory,
        PayrollPluginDbContextFactory payrollPluginDbContextFactory,
        RateFetcher rateFetcher,
        BTCPayNetworkProvider networkProvider,
        IFileService fileService,
        UserManager<ApplicationUser> userManager,
        ISettingsRepository settingsRepository)
    {
        _dbContextFactory = dbContextFactory;
        _payrollPluginDbContextFactory = payrollPluginDbContextFactory;
        _rateFetcher = rateFetcher;
        _networkProvider = networkProvider;
        _fileService = fileService;
        _userManager = userManager;
        _settingsRepository = settingsRepository;
    }
    public StoreData CurrentStore => HttpContext.GetStoreData();

    [HttpGet("~/plugins/{storeId}/payroll/list")]
    public async Task<IActionResult> List(string storeId)
    {
        await using var ctx = _payrollPluginDbContextFactory.CreateContext();
        var payrollInvoices = await ctx.PayrollInvoices
            .Include(data => data.User)
            .Where(p => p.User.StoreId == storeId && p.IsArchived == false)
            .OrderByDescending(data => data.CreatedAt).ToListAsync();

        // triggering saving of admin user id if needed
        var settings = await _settingsRepository.GetSettingAsync<PayrollPluginSettings>();
        settings ??= new PayrollPluginSettings();
        if (settings.AdminAppUserId is null)
        {
            settings.AdminAppUserId = _userManager.GetUserId(User);
            await _settingsRepository.UpdateSetting(settings, nameof(PayrollPluginSettings));
        }
        //

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

    private async Task<IActionResult> payInvoices(string[] selectedItems)
    {
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
            bip21New.QueryParams.Add("label", invoice.User.Name);
            // TODO: Add parameter here on which payroll invoice it is being paid, so that when wallet sends trasaction you can mark it paid
            // bip21New.QueryParams.Add("payrollInvoiceId", invoice.Id);
            bip21.Add(bip21New.ToString());

            invoice.State = PayrollInvoiceState.AwaitingPayment;
        }

        await ctx.SaveChangesAsync();

        return new RedirectToActionResult("WalletSend", "UIWallets", 
            new { walletId = new WalletId(CurrentStore.Id, PayrollPluginConst.BTC_CRYPTOCODE).ToString(), bip21 });
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
            (a.State != PayrollInvoiceState.Completed && a.State != PayrollInvoiceState.Cancelled));

        if (alreadyInvoiceWithAddress)
            ModelState.AddModelError(nameof(model.Destination), "This destination is already specified for another invoice from which payment is in progress");

        if (!ModelState.IsValid)
        {
            model.PayrollUsers = getPayrollUsers(dbPlugin, CurrentStore.Id);
            return View(model);
        }

        // TODO: Make saving of the file and entry in the database atomic
        var settings = await _settingsRepository.GetSettingAsync<PayrollPluginSettings>();
        var uploaded = await _fileService.AddFile(model.Invoice, settings.AdminAppUserId);

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

        dbPlugin.Add(dbPayrollInvoice);
        await dbPlugin.SaveChangesAsync();

        this.TempData.SetStatusMessageModel(new StatusMessageModel()
        {
            Message = $"Invoice uploaded successfully",
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
        [Required]
        public IFormFile Invoice { get; set; }



        public SelectList PayrollUsers { get; set; }
    }
}