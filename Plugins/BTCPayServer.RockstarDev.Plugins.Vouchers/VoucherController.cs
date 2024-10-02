using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.HostedServices;
using BTCPayServer.ModelBinders;
using BTCPayServer.Models;
using BTCPayServer.Models.WalletViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Payouts;
using BTCPayServer.Plugins.PointOfSale.Models;
using BTCPayServer.Rating;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace BTCPayServer.RockstarDev.Plugins.Vouchers;

public class VoucherController : Controller
{
    private readonly PullPaymentHostedService _pullPaymentHostedService;
    private readonly UIStorePullPaymentsController _uiStorePullPaymentsController;
    private readonly ApplicationDbContextFactory _dbContextFactory;
    private readonly DefaultRulesCollection _defaultRulesCollection;
    private readonly UriResolver _uriResolver;
    private readonly PayoutMethodHandlerDictionary _payoutHandlers;
    private readonly StoreRepository _storeRepository;
    private readonly RateFetcher _rateFetcher;
    private readonly BTCPayNetworkProvider _networkProvider;
    private readonly AppService _appService;

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

    private const string CURRENCY = "USD";


    [HttpGet("~/plugins/{storeId}/vouchers/keypad")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [XFrameOptions(XFrameOptionsAttribute.XFrameOptions.Unset)]
    public IActionResult Keypad(string storeId)
    {
        var settings = new PointOfSaleSettings
        {
            Title = "Bitcoin Vouchers"
        };
        var numberFormatInfo = _appService.Currencies.GetNumberFormatInfo(CURRENCY);
        double step = Math.Pow(10, -numberFormatInfo.CurrencyDecimalDigits);
        //var store = new Data.StoreData();
        //var storeBlob = new StoreBlob();

        return View(new ViewPointOfSaleViewModel
        {
            Title = settings.Title,
            //StoreName = store.StoreName,
            //BrandColor = storeBlob.BrandColor,
            //CssFileId = storeBlob.CssFileId,
            //LogoFileId = storeBlob.LogoFileId,
            Step = step.ToString(CultureInfo.InvariantCulture),
            //ViewType = BTCPayServer.Plugins.PointOfSale.PosViewType.Light,
            //ShowCustomAmount = settings.ShowCustomAmount,
            //ShowDiscount = settings.ShowDiscount,
            //ShowSearch = settings.ShowSearch,
            //ShowCategories = settings.ShowCategories,
            //EnableTips = settings.EnableTips,
            //CurrencyCode = settings.Currency,
            //CurrencySymbol = numberFormatInfo.CurrencySymbol,
            CurrencyInfo = new ViewPointOfSaleViewModel.CurrencyInfoData
            {
                CurrencySymbol = string.IsNullOrEmpty(numberFormatInfo.CurrencySymbol) ? settings.Currency : numberFormatInfo.CurrencySymbol,
                Divisibility = numberFormatInfo.CurrencyDecimalDigits,
                DecimalSeparator = numberFormatInfo.CurrencyDecimalSeparator,
                ThousandSeparator = numberFormatInfo.NumberGroupSeparator,
                Prefixed = new[] { 0, 2 }.Contains(numberFormatInfo.CurrencyPositivePattern),
                SymbolSpace = new[] { 2, 3 }.Contains(numberFormatInfo.CurrencyPositivePattern)
            },
            //Items = AppService.Parse(settings.Template, false),
            //ButtonText = settings.ButtonText,
            //CustomButtonText = settings.CustomButtonText,
            //CustomTipText = settings.CustomTipText,
            //CustomTipPercentages = settings.CustomTipPercentages,
            //CustomCSSLink = settings.CustomCSSLink,
            //CustomLogoLink = storeBlob.CustomLogo,
            //AppId = "vouchers",
            StoreId = storeId,
            //Description = settings.Description,
            //EmbeddedCSS = settings.EmbeddedCSS,
            //RequiresRefundEmail = settings.RequiresRefundEmail
        });
    }

    [HttpGet("~/plugins/{storeId}/vouchers/createsatsbill")]
    [Authorize(Policy = Policies.CanCreateNonApprovedPullPayments, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> CreateSatsBill(string storeId, int amount, string image)
    {
        var selectedPaymentMethodIds = new PayoutMethodId[] {
                PayoutMethodId.Parse("BTC-CHAIN"),
                PayoutMethodId.Parse("BTC-LN")
            };
        var res = await _pullPaymentHostedService.CreatePullPayment(new HostedServices.CreatePullPayment()
        {
            Name = $"Voucher {amount} Sats",
            Description = image,
            Amount = amount,
            Currency = "SATS",
            StoreId = storeId,
            PayoutMethods = selectedPaymentMethodIds,
            BOLT11Expiration = TimeSpan.FromDays(21),
            AutoApproveClaims = true
        });
        //this.TempData.SetStatusMessageModel(new StatusMessageModel()
        //{
        //    Message = "Pull payment request created",
        //    Severity = StatusMessageModel.StatusSeverity.Success
        //});
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

        if (pp == null)
        {
            return NotFound();
        }

        var blob = pp.GetBlob();
        if (!blob.Name.StartsWith("Voucher"))
        {
            return NotFound();
        }

        var now = DateTimeOffset.UtcNow;
        var store = await _storeRepository.FindStore(pp.StoreId);
        var storeBlob = store.GetStoreBlob();
        var progress = _pullPaymentHostedService.CalculatePullPaymentProgress(pp, now);
        return View(await new VoucherViewModel()
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
    public async Task<IActionResult> ListVouchers(string storeId)
    {

        var now = DateTimeOffset.UtcNow;
        await using var ctx = _dbContextFactory.CreateContext();
        var ppsQuery = await ctx.PullPayments
            .Include(data => data.Payouts)
            .Where(p => p.StoreId == storeId && p.Archived == false)
            .OrderByDescending(data => data.StartDate).ToListAsync();

        var vouchers = ppsQuery.Select(pp => (PullPayment: pp, Blob: pp.GetBlob())).Where(blob => blob.Blob.Name.StartsWith("Voucher")).ToList();

        var paymentMethods = _payoutHandlers.GetSupportedPayoutMethods(HttpContext.GetStoreData());
        if (!paymentMethods.Any())
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = "You must enable at least one payment method before creating a voucher.",
                Severity = StatusMessageModel.StatusSeverity.Error
            });
            return RedirectToAction(nameof(UIStoresController.Dashboard), "UIStores", new { storeId });
        }
        return View(vouchers.Select(tuple => new VoucherViewModel()
        {
            Amount = tuple.PullPayment.Limit,
            Currency = tuple.PullPayment.Currency,
            Id = tuple.PullPayment.Id,
            Name = tuple.Blob.Name,
            Description = tuple.Blob.Description,
            PayoutMethods = tuple.Blob.SupportedPayoutMethods,
            Progress = _pullPaymentHostedService.CalculatePullPaymentProgress(tuple.PullPayment, now)
        }).ToList());
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
                Message = "You must enable at least one payment method before creating a voucher.",
                Severity = StatusMessageModel.StatusSeverity.Error
            });
            return RedirectToAction(nameof(UIStoresController.Dashboard), "UIStores", new { storeId });
        }
        return View();
    }

    [HttpPost("~/plugins/{storeId}/vouchers/create")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> CreateVoucher(string storeId,
        [ModelBinder(typeof(InvariantDecimalModelBinder))] decimal amount)
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

        var storeBlob = HttpContext.GetStoreData().GetStoreBlob();
        var currency = CURRENCY;

        var rate = await _rateFetcher.FetchRate(new CurrencyPair(currency, "BTC"),
            storeBlob.GetRateRules(_defaultRulesCollection), new StoreIdRateContext(storeId), CancellationToken.None);
        if (rate.BidAsk == null)
        {
            TempData[WellKnownTempData.ErrorMessage] = "Currency is not supported";
            return View();
        }

        string description = string.Empty;
        var satsAmount = Math.Floor(amount * rate.BidAsk.Bid * 100_000_000);
        var amountInBtc = satsAmount / 100_000_000;
        if (currency != "BTC")
        {
            description = $"{amount} {currency} voucher redeemable for {amountInBtc} BTC";
        }

        var pp = await _pullPaymentHostedService.CreatePullPayment(new CreatePullPayment()
        {
            Amount = amountInBtc,
            Currency = "BTC",
            Name = "Voucher " + Encoders.Base58.EncodeData(RandomUtils.GetBytes(6)),
            Description = description,
            StoreId = storeId,
            PayoutMethods = paymentMethods.ToArray(),
            AutoApproveClaims = true
        });

        return RedirectToAction(nameof(View), new { id = pp });
    }

    [HttpGet("~/plugins/vouchers/{id}")]
    [AllowAnonymous]
    public async new Task<IActionResult> View(string id)
    {
        await using var ctx = _dbContextFactory.CreateContext();
        var pp = await ctx.PullPayments
            .Include(data => data.Payouts)
            .SingleOrDefaultAsync(p => p.Id == id && p.Archived == false);

        if (pp == null)
        {
            return NotFound();
        }

        var blob = pp.GetBlob();
        if (!blob.Name.StartsWith("Voucher"))
        {
            return NotFound();
        }

        var now = DateTimeOffset.UtcNow;
        var store = await _storeRepository.FindStore(pp.StoreId);
        var storeBlob = store.GetStoreBlob();
        var progress = _pullPaymentHostedService.CalculatePullPaymentProgress(pp, now);
        return View(await new VoucherViewModel()
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




    public class VoucherViewModel
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; }
        public PayoutMethodId[] PayoutMethods { get; set; }
        public PullPaymentsModel.PullPaymentModel.ProgressModel Progress { get; set; }
        public string StoreName { get; set; }
        public string LogoUrl { get; set; }
        public string BrandColor { get; set; }
        public string CssUrl { get; set; }
        public bool SupportsLNURL { get; set; }
        public string Description { get; set; }
        public async Task<VoucherViewModel> SetStoreBranding(HttpRequest request, UriResolver uriResolver, StoreBlob storeBlob)
        {
            var branding = await StoreBrandingViewModel.CreateAsync(request, uriResolver, storeBlob);
            LogoUrl = branding.LogoUrl;
            CssUrl = branding.CssUrl;
            BrandColor = branding.BrandColor;
            return this;
        }
    }
}