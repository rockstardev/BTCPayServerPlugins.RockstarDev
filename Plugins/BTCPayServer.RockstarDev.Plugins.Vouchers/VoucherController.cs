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
using BTCPayServer.Models.WalletViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.PointOfSale.Models;
using BTCPayServer.Rating;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
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
    private readonly IEnumerable<IPayoutHandler> _payoutHandlers;
    private readonly StoreRepository _storeRepository;
    private readonly RateFetcher _rateFetcher;
    private readonly BTCPayNetworkProvider _networkProvider;
    private readonly AppService _appService;

    public VoucherController(PullPaymentHostedService pullPaymentHostedService,
        UIStorePullPaymentsController uiStorePullPaymentsController,
        ApplicationDbContextFactory dbContextFactory,
        IEnumerable<IPayoutHandler> payoutHandlers, StoreRepository storeRepository, RateFetcher rateFetcher, BTCPayNetworkProvider networkProvider,
            AppService appService)
    {
        _pullPaymentHostedService = pullPaymentHostedService;
        _uiStorePullPaymentsController = uiStorePullPaymentsController;
        _dbContextFactory = dbContextFactory;
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
    public async Task<IActionResult> Keypad(string storeId)
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


    [HttpGet("~/plugins/{storeId}/vouchers")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> ListVouchers(string storeId)
    {

        var now = DateTimeOffset.UtcNow;
        await using var ctx = _dbContextFactory.CreateContext();
        var ppsQuery = await ctx.PullPayments
            .Include(data => data.Payouts)
            .Where(p => p.StoreId == storeId && p.Archived == false)
            .OrderByDescending(data => data.Id).ToListAsync();

        var vouchers = ppsQuery.Select(pp => (PullPayment: pp, Blob: pp.GetBlob())).Where(blob => blob.Blob.Name.StartsWith("Voucher")).ToList();

        var paymentMethods = await _payoutHandlers.GetSupportedPaymentMethods(HttpContext.GetStoreData());
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
            Amount = tuple.Blob.Limit,
            Currency = tuple.Blob.Currency,
            Id = tuple.PullPayment.Id,
            Name = tuple.Blob.Name,
            PaymentMethods = tuple.Blob.SupportedPaymentMethods,
            Progress = _pullPaymentHostedService.CalculatePullPaymentProgress(tuple.PullPayment, now)
        }).ToList());
    }

    [HttpGet("~/plugins/{storeId}/vouchers/create")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> CreateVoucher(string storeId)
    {
        var paymentMethods = await _payoutHandlers.GetSupportedPaymentMethods(HttpContext.GetStoreData());
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
        var paymentMethods = await _payoutHandlers.GetSupportedPaymentMethods(HttpContext.GetStoreData());
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
            storeBlob.GetRateRules(_networkProvider), CancellationToken.None);
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
            PaymentMethodIds = paymentMethods.ToArray(),
            AutoApproveClaims = true
        });

        return RedirectToAction(nameof(View), new { id = pp });
    }

    [HttpGet("~/plugins/vouchers/{id}")]
    [AllowAnonymous]
    public async Task<IActionResult> View(string id)
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
        return View(new VoucherViewModel()
        {
            Amount = blob.Limit,
            Currency = blob.Currency,
            Id = pp.Id,
            Name = blob.Name,
            PaymentMethods = blob.SupportedPaymentMethods,
            Progress = progress,
            StoreName = store.StoreName,
            BrandColor = storeBlob.BrandColor,
            CssFileId = storeBlob.CssFileId,
            LogoFileId = storeBlob.LogoFileId,
            SupportsLNURL = _pullPaymentHostedService.SupportsLNURL(blob),
            Description = blob.Description
        });
    }




    public class VoucherViewModel
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; }
        public PaymentMethodId[] PaymentMethods { get; set; }
        public PullPaymentsModel.PullPaymentModel.ProgressModel Progress { get; set; }
        public string StoreName { get; set; }
        public string LogoFileId { get; set; }
        public string BrandColor { get; set; }
        public string CssFileId { get; set; }
        public bool SupportsLNURL { get; set; }
        public string Description { get; set; }
    }
}