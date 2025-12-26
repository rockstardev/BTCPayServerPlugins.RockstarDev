using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.HostedServices;
using BTCPayServer.ModelBinders;
using BTCPayServer.Models;
using BTCPayServer.Payouts;
using BTCPayServer.Plugins.PointOfSale;
using BTCPayServer.Plugins.PointOfSale.Models;
using BTCPayServer.Rating;
using BTCPayServer.RockstarDev.Plugins.Vouchers.ViewModel;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace BTCPayServer.RockstarDev.Plugins.Vouchers;

public class VoucherController : Controller
{
    private readonly AppService _appService;
    private readonly ApplicationDbContextFactory _dbContextFactory;
    private readonly DefaultRulesCollection _defaultRulesCollection;
    private readonly BTCPayNetworkProvider _networkProvider;
    private readonly PayoutMethodHandlerDictionary _payoutHandlers;
    private readonly PullPaymentHostedService _pullPaymentHostedService;
    private readonly RateFetcher _rateFetcher;
    private readonly StoreRepository _storeRepository;
    private readonly UIStorePullPaymentsController _uiStorePullPaymentsController;
    private readonly UriResolver _uriResolver;

    public VoucherController(PullPaymentHostedService pullPaymentHostedService,
        UIStorePullPaymentsController uiStorePullPaymentsController,
        ApplicationDbContextFactory dbContextFactory,
        DefaultRulesCollection defaultRulesCollection,
        UriResolver uriResolver,
        PayoutMethodHandlerDictionary payoutHandlers, StoreRepository storeRepository, RateFetcher rateFetcher, BTCPayNetworkProvider networkProvider,
        AppService appService)
    {
        _pullPaymentHostedService = pullPaymentHostedService;
        _uiStorePullPaymentsController = uiStorePullPaymentsController;
        _dbContextFactory = dbContextFactory;
        _defaultRulesCollection = defaultRulesCollection;
        _uriResolver = uriResolver;
        _payoutHandlers = payoutHandlers;
        _storeRepository = storeRepository;
        _rateFetcher = rateFetcher;
        _networkProvider = networkProvider;
        _appService = appService;
    }

    public StoreData CurrentStore => HttpContext.GetStoreData();

    [HttpGet("~/plugins/{storeId}/vouchers/keypad")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [XFrameOptions(XFrameOptionsAttribute.XFrameOptions.Unset)]
    public async Task<IActionResult> Keypad(string storeId)
    {
        if (CurrentStore == null)
            return NotFound();

        var store = await _storeRepository.FindStore(CurrentStore.Id);
        if (store == null)
            return NotFound();

        var pm = PayoutMethodId.Parse("BTC-LN");
        var paymentMethods = _payoutHandlers.GetSupportedPayoutMethods(HttpContext.GetStoreData());
        if (!paymentMethods.Any() || !paymentMethods.TryGetValue(pm, out var handler) || handler == null)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = "You must enable Lightning payouts before creating a voucher.",
                Severity = StatusMessageModel.StatusSeverity.Error
            });
            return RedirectToAction(nameof(UIStoresController.Dashboard), "UIStores", new { storeId });
        }

        var app = await GetVoucherAppData();
        if (app == null)
            return NotFound();

        var settings = app.GetSettings<VoucherPluginAppType.AppConfig>();
        var numberFormatInfo = _appService.Currencies.GetNumberFormatInfo(settings.Currency);
        var step = Math.Pow(10, -numberFormatInfo.CurrencyDecimalDigits);
        var storeBlob = store.GetStoreBlob();
        return View(new ViewPointOfSaleViewModel
        {
            Title = settings.Title,
            StoreName = store.StoreName,
            StoreBranding = await StoreBrandingViewModel.CreateAsync(Request, _uriResolver, storeBlob),
            Step = step.ToString(CultureInfo.InvariantCulture),
            ViewType = PosViewType.Light,
            ShowItems = settings.ShowItems,
            ShowCustomAmount = settings.ShowCustomAmount,
            ShowDiscount = settings.ShowDiscount,
            ShowSearch = settings.ShowSearch,
            ShowCategories = settings.ShowCategories,
            EnableTips = settings.EnableTips,
            CurrencyCode = settings.Currency,
            CurrencySymbol = numberFormatInfo.CurrencySymbol,
            CurrencyInfo = new ViewPointOfSaleViewModel.CurrencyInfoData
            {
                CurrencySymbol = string.IsNullOrEmpty(numberFormatInfo.CurrencySymbol) ? settings.Currency : numberFormatInfo.CurrencySymbol,
                Divisibility = numberFormatInfo.CurrencyDecimalDigits,
                DecimalSeparator = numberFormatInfo.CurrencyDecimalSeparator,
                ThousandSeparator = numberFormatInfo.NumberGroupSeparator,
                Prefixed = new[] { 0, 2 }.Contains(numberFormatInfo.CurrencyPositivePattern),
                SymbolSpace = new[] { 2, 3 }.Contains(numberFormatInfo.CurrencyPositivePattern)
            },
            Items = AppService.Parse(settings.Template, false),
            ButtonText = settings.ButtonText,
            CustomButtonText = settings.CustomButtonText,
            CustomTipText = settings.CustomTipText,
            CustomTipPercentages = settings.CustomTipPercentages,
            DefaultTaxRate = settings.DefaultTaxRate,
            AppId = app.Id,
            StoreId = store.Id,
            HtmlLang = settings.HtmlLang,
            HtmlMetaTags = settings.HtmlMetaTags,
            Description = settings.Description,
        });
    }

    [HttpGet("~/plugins/{storeId}/vouchers/createsatsbill")]
    [Authorize(Policy = Policies.CanCreateNonApprovedPullPayments, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> CreateSatsBill(string storeId, int amount, string image)
    {
        var selectedPaymentMethodIds = new[] { PayoutMethodId.Parse("BTC-CHAIN"), PayoutMethodId.Parse("BTC-LN") };
        var res = await _pullPaymentHostedService.CreatePullPayment(HttpContext.GetStoreData(), new()
        {
            Name = $"Voucher {amount} Sats",
            Description = image,
            Amount = amount,
            Currency = "SATS",
            PayoutMethods = selectedPaymentMethodIds.Select(c => c.ToString()).ToArray(),
            BOLT11Expiration = TimeSpan.FromDays(21),
            AutoApproveClaims = true
        });
        return RedirectToAction(nameof(ViewPrintSatsBill), new { id = res });
    }

    [AllowAnonymous]
    [HttpGet("~/plugins/vouchers/{id}/viewprintsatsbill")]
    public async Task<IActionResult> ViewPrintSatsBill(string id)
    {
        await using var ctx = _dbContextFactory.CreateContext();
        var pp = await ctx.PullPayments
            .Include(data => data.Payouts)
            .SingleOrDefaultAsync(p => p.Id == id && p.Archived == false);

        if (pp == null) return NotFound();

        var blob = pp.GetBlob();
        if (!blob.Name.StartsWith("Voucher")) return NotFound();

        var now = DateTimeOffset.UtcNow;
        var store = await _storeRepository.FindStore(pp.StoreId);
        var storeBlob = store.GetStoreBlob();
        var progress = _pullPaymentHostedService.CalculatePullPaymentProgress(pp, now);
        return View(await new VoucherViewModel
        {
            Amount = pp.Limit,
            Currency = pp.Currency,
            Id = pp.Id,
            Name = blob.Name,
            PayoutMethods = blob.SupportedPayoutMethods,
            Progress = progress,
            StoreName = store.StoreName,
            SupportsLNURL = _pullPaymentHostedService.SupportsLNURL(pp, blob),
            Description = blob.Description
        }.SetStoreBranding(Request, _uriResolver, storeBlob));
    }


    [HttpGet("~/plugins/{storeId}/vouchers")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> ListVouchers(string storeId, VoucherPaymentState voucherPaymentState)
    {
        var now = DateTimeOffset.UtcNow;
        await GetVoucherAppData();
        await using var ctx = _dbContextFactory.CreateContext();
        var query = ctx.PullPayments.Include(c => c.Payouts).Where(p => p.StoreId == storeId);
        query = voucherPaymentState switch
        {
            VoucherPaymentState.Active => query.Where(p => !p.Archived),
            // VoucherPaymentState.Expired => query.Where(p => !p.Archived && p.EndDate <= now), Right now EndDate is null
            VoucherPaymentState.Archived => query.Where(p => p.Archived),
            _ => query
        };
        var ppsQuery = await query.OrderByDescending(p => p.StartDate).ToListAsync();

        var vouchers = ppsQuery.Select(pp => (PullPayment: pp, Blob: pp.GetBlob())).Where(blob => blob.Blob.Name.StartsWith("Voucher")).ToList();
        var vm = vouchers.Select(tuple => new VoucherViewModel
        {
            Amount = tuple.PullPayment.Limit,
            Currency = tuple.PullPayment.Currency,
            Id = tuple.PullPayment.Id,
            Name = tuple.Blob.Name,
            Description = tuple.Blob.Description,
            PayoutMethods = tuple.Blob.SupportedPayoutMethods,
            Progress = _pullPaymentHostedService.CalculatePullPaymentProgress(tuple.PullPayment, now)
        }).ToList();
        return View(new ListVoucherViewModel { Vouchers = vm, ActiveState = voucherPaymentState });
    }


    [HttpGet("~/plugins/{storeId}/vouchers/{pullPaymentId}/archive")]
    [Authorize(Policy = Policies.CanArchivePullPayments, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public IActionResult ArchiveVoucher(string storeId,
        string pullPaymentId)
    {
        return View("Confirm", new ConfirmModel("Archive Voucher", "Do you really want to archive the pull payment?", "Archive"));
    }

    [HttpPost("~/plugins/{storeId}/vouchers/{pullPaymentId}/archive")]
    [Authorize(Policy = Policies.CanArchivePullPayments, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> ArchiveVoucherPost(string storeId,
        string pullPaymentId)
    {
        await _pullPaymentHostedService.Cancel(new PullPaymentHostedService.CancelRequest(pullPaymentId));
        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Message = "Voucher archived successfully",
            Severity = StatusMessageModel.StatusSeverity.Success
        });
        return RedirectToAction(nameof(ListVouchers), new { storeId });
    }

    [HttpGet("~/plugins/{storeId}/vouchers/create")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public IActionResult CreateVoucher(string storeId)
    {
        var paymentMethods = _payoutHandlers.GetSupportedPayoutMethods(HttpContext.GetStoreData());
        if (!paymentMethods.Any())
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = "You must enable at least one payment method before creating a voucher.", Severity = StatusMessageModel.StatusSeverity.Error
            });
            return RedirectToAction(nameof(UIStoresController.Dashboard), "UIStores", new { storeId });
        }

        return View();
    }

    [HttpPost("~/plugins/{storeId}/vouchers/create")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> CreateVoucher(string storeId,
        [ModelBinder(typeof(InvariantDecimalModelBinder))]
        decimal amount)
    {
        ModelState.Clear();
        var paymentMethods = _payoutHandlers.GetSupportedPayoutMethods(HttpContext.GetStoreData());
        if (!paymentMethods.Any())
        {
            TempData[WellKnownTempData.ErrorMessage] = "You must enable at least one payment method before creating a voucher.";
            return RedirectToAction(nameof(UIStoresController.Dashboard), "UIStores", new { storeId });
        }

        if (amount <= 0)
        {
            TempData[WellKnownTempData.ErrorMessage] = "Amount must be greater than 0";
            return View();
        }

        var app = await GetVoucherAppData();
        var settings = app.GetSettings<VoucherPluginAppType.AppConfig>();
        var store = HttpContext.GetStoreData();
        var storeBlob = store.GetStoreBlob();

        var rate = await _rateFetcher.FetchRate(new CurrencyPair(settings.Currency, "BTC"),
            storeBlob.GetRateRules(_defaultRulesCollection), new StoreIdRateContext(storeId), CancellationToken.None);
        if (rate.BidAsk == null)
        {
            TempData[WellKnownTempData.ErrorMessage] = "Currency is not supported";
            return View();
        }

        var description = string.Empty;
        var satsAmount = Math.Floor(amount * rate.BidAsk.Bid * 100_000_000);
        var amountInBtc = satsAmount / 100_000_000;
        if (settings.Currency != "BTC") description = $"{amount} {settings.Currency} voucher redeemable for {amountInBtc} BTC";

        var pp = await _pullPaymentHostedService.CreatePullPayment(store, new()
        {
            Amount = amountInBtc,
            Currency = "BTC",
            Name = "Voucher " + Encoders.Base58.EncodeData(RandomUtils.GetBytes(6)),
            Description = description,
            PayoutMethods = paymentMethods.Select(c => c.ToString()).ToArray(),
            AutoApproveClaims = true
        });

        return RedirectToAction(nameof(View), new { id = pp });
    }

    [HttpGet("~/plugins/vouchers/{id}")]
    [AllowAnonymous]
    public new async Task<IActionResult> View(string id)
    {
        await using var ctx = _dbContextFactory.CreateContext();
        var pp = await ctx.PullPayments
            .Include(data => data.Payouts)
            .SingleOrDefaultAsync(p => p.Id == id && p.Archived == false);

        if (pp == null) return NotFound();

        var blob = pp.GetBlob();
        if (!blob.Name.StartsWith("Voucher")) return NotFound();

        var now = DateTimeOffset.UtcNow;
        var store = await _storeRepository.FindStore(pp.StoreId);
        var storeBlob = store.GetStoreBlob();
        var progress = _pullPaymentHostedService.CalculatePullPaymentProgress(pp, now);
        return View(await new VoucherViewModel
        {
            Amount = pp.Limit,
            Currency = pp.Currency,
            Id = pp.Id,
            Name = blob.Name,
            PayoutMethods = blob.SupportedPayoutMethods,
            Progress = progress,
            StoreName = store.StoreName,
            SupportsLNURL = _pullPaymentHostedService.SupportsLNURL(pp, blob),
            Description = blob.Description
        }.SetStoreBranding(Request, _uriResolver, storeBlob));
    }

    public async Task CreateVoucherAppData(string storeId)
    {
        string defaultCurrency = (await _storeRepository.FindStore(storeId)).GetStoreBlob().DefaultCurrency;
        var settings = new VoucherPluginAppType.AppConfig
        {
            Currency = defaultCurrency.Trim().ToUpperInvariant(),
            Title = "Bitcoin Vouchers",
            CustomButtonText = "Buy Voucher",
            ShowCustomAmount = true,
            ShowCategories = true,
            DefaultView = PosViewType.Light,
            DefaultTaxRate = 0
        };
        var app = new AppData()
        {
            Name = VoucherPluginAppType.AppType,
            AppType = VoucherPluginAppType.AppType,
            StoreDataId = storeId
        };
        app.SetSettings(settings);
        await _appService.UpdateOrCreateApp(app, sendEvents: false);
    }

    public async Task<AppData> GetVoucherAppData()
    {
        var apps = await _appService.GetApps(VoucherPluginAppType.AppType);
        var app = apps.FirstOrDefault(c => c.StoreDataId == CurrentStore.Id);
        if (app != null)
            return app;

        await CreateVoucherAppData(CurrentStore.Id);
        apps = await _appService.GetApps(VoucherPluginAppType.AppType);
        return apps.FirstOrDefault(c => c.StoreDataId == CurrentStore.Id);
    }
}
