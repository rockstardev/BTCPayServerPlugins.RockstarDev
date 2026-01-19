using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Dom;
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
using BTCPayServer.Payouts;
using BTCPayServer.Plugins.PointOfSale;
using BTCPayServer.Plugins.PointOfSale.Controllers;
using BTCPayServer.Plugins.PointOfSale.Models;
using BTCPayServer.Rating;
using BTCPayServer.RockstarDev.Plugins.Vouchers.Utility;
using BTCPayServer.RockstarDev.Plugins.Vouchers.ViewModel;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace BTCPayServer.RockstarDev.Plugins.Vouchers;

public class VoucherController : Controller
{
    private readonly AppService _appService;
    private readonly RateFetcher _rateFetcher;
    private readonly UriResolver _uriResolver;
    private readonly IFileService _fileService;
    private readonly StoreRepository _storeRepository;
    private readonly BTCPayNetworkProvider _networkProvider;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly PayoutMethodHandlerDictionary _payoutHandlers;
    private readonly ApplicationDbContextFactory _dbContextFactory;
    private readonly DefaultRulesCollection _defaultRulesCollection;
    private readonly PullPaymentHostedService _pullPaymentHostedService;
    private readonly UIStorePullPaymentsController _uiStorePullPaymentsController;

    public VoucherController(PullPaymentHostedService pullPaymentHostedService,
        UIStorePullPaymentsController uiStorePullPaymentsController,
        ApplicationDbContextFactory dbContextFactory, UserManager<ApplicationUser> userManager,
        DefaultRulesCollection defaultRulesCollection, UriResolver uriResolver, IFileService fileService,
        PayoutMethodHandlerDictionary payoutHandlers, StoreRepository storeRepository, RateFetcher rateFetcher, BTCPayNetworkProvider networkProvider,
        AppService appService)
    {
        _appService = appService;
        _uriResolver = uriResolver;
        _fileService = fileService;
        _rateFetcher = rateFetcher;
        _userManager = userManager;
        _payoutHandlers = payoutHandlers;
        _storeRepository = storeRepository;
        _networkProvider = networkProvider;
        _dbContextFactory = dbContextFactory;
        _defaultRulesCollection = defaultRulesCollection;
        _pullPaymentHostedService = pullPaymentHostedService;
        _uiStorePullPaymentsController = uiStorePullPaymentsController;
    }

    public StoreData CurrentStore => HttpContext.GetStoreData();
    private string GetUserId() => _userManager.GetUserId(User);

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

        var posApps = await _appService.GetApps(PointOfSaleAppType.AppType);
        List<StorePosAppItem> posAppItems = new();
        foreach (var item in posApps.Where(c => c.StoreDataId == CurrentStore.Id))
        {
            var posSettings = item.GetSettings<PointOfSaleSettings>();
            Enum.TryParse<PosViewType>(posSettings.DefaultView.ToString(), true, out var posView);
            if (posView == PosViewType.Light)
            {
                posAppItems.Add(new StorePosAppItem
                {
                    Name = item.Name,
                    Url = Url.Action(nameof(UIPointOfSaleController.ViewPointOfSale), "UIPointOfSale", new { appId = item.Id }, Request.Scheme)
                });
            }
        }

        var settings = app.GetSettings<VoucherPluginAppType.AppConfig>();
        var numberFormatInfo = _appService.Currencies.GetNumberFormatInfo(settings.Currency);
        var step = Math.Pow(10, -numberFormatInfo.CurrencyDecimalDigits);
        var storeBlob = store.GetStoreBlob();
        return View(new VoucherPointOfSaleViewModel
        {
            PoSApps = posAppItems,
            CurrentAppId = app.Id,
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

    [HttpGet("~/plugins/vouchers/{id}")]
    [AllowAnonymous]
    public new async Task<IActionResult> View(string id)
    {
        await using var ctx = _dbContextFactory.CreateContext();
        var pp = await ctx.PullPayments.Include(data => data.Payouts).SingleOrDefaultAsync(p => p.Id == id && p.Archived == false);

        if (pp == null)
            return NotFound();

        var blob = pp.GetBlob();
        if (!blob.Name.StartsWith("Voucher"))
            return NotFound();

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


    [HttpGet("~/plugins/{storeId}/vouchers/createsatsbill")]
    [Authorize(Policy = Policies.CanCreateNonApprovedPullPayments, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> CreateSatsBill(string storeId, int satsAmount, string imageKey)
    {
        var selectedPaymentMethodIds = new[] { PayoutMethodId.Parse("BTC-CHAIN"), PayoutMethodId.Parse("BTC-LN") };
        var amountInBtc = satsAmount / 100_000_000m;
        var settings = await _storeRepository.GetSettingAsync<VoucherSettings>(CurrentStore.Id, VoucherPlugin.SettingsName) ?? new VoucherSettings();
        
        var matchedImage = settings.Images.FirstOrDefault(c => c.Name.ToLower() == imageKey.Trim().ToLower());
        if (matchedImage == null)
        {
            TempData[WellKnownTempData.ErrorMessage] = "Invalid voucher image";
            return RedirectToAction(nameof(ListVouchers), new { storeId });
        }
        
        var res = await _pullPaymentHostedService.CreatePullPayment(HttpContext.GetStoreData(), new()
        {
            Amount = amountInBtc,
            Currency = "BTC",
            Name = "Voucher " + Encoders.Base58.EncodeData(RandomUtils.GetBytes(6)),
            Description = VoucherImages.GetImageFile(imageKey),
            PayoutMethods = selectedPaymentMethodIds.Select(c => c.ToString()).ToArray(),
            BOLT11Expiration = TimeSpan.FromDays(21),
            AutoApproveClaims = true
        });
        return RedirectToAction(nameof(ViewPrintSatsBill), new { storeId = CurrentStore.Id, id = res, imageKey = matchedImage.Key });
    }

    [AllowAnonymous]
    [HttpGet("~/plugins/{storeId}/vouchers/{id}/viewprintsatsbill")]
    public async Task<IActionResult> ViewPrintSatsBill(string storeId, string id, string imageKey)
    {
        await using var ctx = _dbContextFactory.CreateContext();
        var pp = await ctx.PullPayments.Include(data => data.Payouts)
            .SingleOrDefaultAsync(p => p.Id == id && p.Archived == false);

        if (pp == null) return NotFound();

        var blob = pp.GetBlob();
        if (!blob.Name.StartsWith("Voucher")) return NotFound();

        var settings = await _storeRepository.GetSettingAsync<VoucherSettings>(storeId, VoucherPlugin.SettingsName) ?? new VoucherSettings();
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
            Description = blob.Description,
            VoucherImage = settings.Images.First(c => c.Key == imageKey).FileUrl
        }.SetStoreBranding(Request, _uriResolver, storeBlob));
    }

    [AllowAnonymous]
    [HttpGet("~/plugins/{storeId}/vouchers/{id}/printvoucher")]
    public async Task<IActionResult> PrintVoucher(string storeId, string id)
    {
        await using var ctx = _dbContextFactory.CreateContext();
        var pp = await ctx.PullPayments.Include(data => data.Payouts)
            .SingleOrDefaultAsync(p => p.Id == id && p.Archived == false);

        if (pp == null)
            return NotFound();

        var blob = pp.GetBlob();
        if (blob == null || !blob.Name.StartsWith("Voucher"))
            return NotFound();

        var voucherSettings = await _storeRepository.GetSettingAsync<VoucherSettings>(storeId, VoucherPlugin.SettingsName) ?? new VoucherSettings();
        return HandleImageRedirectView(voucherSettings, nameof(ListVouchers), storeId, id);
    }


    [HttpGet("~/plugins/{storeId}/vouchers")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> ListVouchers(string storeId, string searchText, VoucherPaymentState voucherPaymentState)
    {
        var now = DateTimeOffset.UtcNow;
        await GetVoucherAppData();
        await using var ctx = _dbContextFactory.CreateContext();
        var query = ctx.PullPayments.Include(c => c.Payouts).Where(p => p.StoreId == storeId);
        query = voucherPaymentState switch
        {
            VoucherPaymentState.Active => query.Where(p => !p.Archived),
            VoucherPaymentState.Archived => query.Where(p => p.Archived),
            _ => query
        };
        var pullPayments = await query.OrderByDescending(p => p.StartDate).ToListAsync();

        var vouchers = pullPayments.Select(pp => (PullPayment: pp, Blob: pp.GetBlob())).Where(x => x.Blob.Name.StartsWith("Voucher"))
        .Where(x => string.IsNullOrWhiteSpace(searchText) || x.Blob.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase) || x.PullPayment.Id.Contains(searchText, StringComparison.OrdinalIgnoreCase))
        .Select(x => new VoucherViewModel
        {
            Id = x.PullPayment.Id,
            Name = x.Blob.Name,
            Description = x.Blob.Description,
            Amount = x.PullPayment.Limit,
            Currency = x.PullPayment.Currency,
            PayoutMethods = x.Blob.SupportedPayoutMethods,
            Progress = _pullPaymentHostedService.CalculatePullPaymentProgress(x.PullPayment, now)
        }).ToList();
        return View(new ListVoucherViewModel { Vouchers = vouchers, ActiveState = voucherPaymentState, SearchText = searchText });
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
        var voucherSettings = await _storeRepository.GetSettingAsync<VoucherSettings>(CurrentStore.Id, VoucherPlugin.SettingsName) ?? new VoucherSettings();
        var app = await GetVoucherAppData();
        var appSettings = app.GetSettings<VoucherPluginAppType.AppConfig>();
        var store = HttpContext.GetStoreData();
        var storeBlob = store.GetStoreBlob();

        var rate = await _rateFetcher.FetchRate(new CurrencyPair(appSettings.Currency, "BTC"),
            storeBlob.GetRateRules(_defaultRulesCollection), new StoreIdRateContext(storeId), CancellationToken.None);
        if (rate.BidAsk == null)
        {
            TempData[WellKnownTempData.ErrorMessage] = "Currency is not supported";
            return View();
        }
        var bidRate = rate.BidAsk.Bid;
        if (voucherSettings.SpreadEnabled)
        {
            bidRate = bidRate * (1 + voucherSettings.SpreadPercentage / 100m);
        }
        var satsAmount = Math.Floor(amount * bidRate * 100_000_000);
        var amountInBtc = satsAmount / 100_000_000;
        var description = string.Empty;
        if (appSettings.Currency != "BTC") description = $"{amount} {appSettings.Currency} voucher redeemable for {amountInBtc} BTC";

        var pp = await _pullPaymentHostedService.CreatePullPayment(store, new()
        {
            Amount = amountInBtc,
            Currency = "BTC",
            Name = "Voucher " + Encoders.Base58.EncodeData(RandomUtils.GetBytes(6)),
            Description = description,
            PayoutMethods = paymentMethods.Select(c => c.ToString()).ToArray(),
            AutoApproveClaims = true
        });
        return HandleImageRedirectView(voucherSettings, nameof(Keypad), storeId, pp);
    }

    [HttpGet("~/plugins/{storeId}/vouchers/{pullPaymentId}/archive")]
    [Authorize(Policy = Policies.CanArchivePullPayments, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public IActionResult ArchiveVoucher(string storeId, string pullPaymentId)
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


    [HttpGet("~/plugins/{storeId}/vouchers/settings")]
    [Authorize(Policy = Policies.CanModifyStoreSettings)]
    public async Task<IActionResult> VoucherSettings(string storeId)
    {
        if (CurrentStore == null)
            return NotFound();

        var settings = await _storeRepository.GetSettingAsync<VoucherSettings>(CurrentStore.Id, VoucherPlugin.SettingsName) ?? new VoucherSettings();
        if (!settings.Images.Any())
        {
            settings = await UploadDefaultVoucherImages(settings);
        }
        return View(new VoucherSettingsViewModel
        {
            UseRandomImage = settings.UseRandomImage,
            SpreadEnabled = settings.SpreadEnabled,
            SpreadPercentage = settings.SpreadPercentage,
            SelectedVoucherImage = settings.SelectedVoucherImage,
            FunModeEnabled = settings.FunModeEnabled,
            VoucherOptions = settings.Images.Where(c => c.Enabled).Select(c => c.Name).ToList()
        });
    }

    [HttpPost("~/plugins/{storeId}/vouchers/settings")]
    [Authorize(Policy = Policies.CanModifyStoreSettings)]
    public async Task<IActionResult> VoucherSettings(string storeId, VoucherSettingsViewModel model)
    {
        if (CurrentStore == null)
            return NotFound();

        var settings = await _storeRepository.GetSettingAsync<VoucherSettings>(CurrentStore.Id, VoucherPlugin.SettingsName) ?? new VoucherSettings();
        settings.SpreadPercentage = model.SpreadPercentage;
        settings.SpreadEnabled = model.SpreadEnabled;
        settings.FunModeEnabled = model.FunModeEnabled;
        settings.UseRandomImage = model.UseRandomImage;
        settings.SelectedVoucherImage = model.SelectedVoucherImage;
        await _storeRepository.UpdateSetting(CurrentStore.Id, VoucherPlugin.SettingsName, settings);
        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Severity = StatusMessageModel.StatusSeverity.Success,
            Message = "Voucher settings updated"
        });
        return RedirectToAction(nameof(VoucherSettings), new { storeId });
    }


    [HttpGet("~/plugins/{storeId}/vouchers/settings/template")]
    [Authorize(Policy = Policies.CanModifyStoreSettings)]
    public async Task<IActionResult> BillTemplateSettings(string storeId)
    {
        var settings = await _storeRepository.GetSettingAsync<VoucherSettings>(storeId, VoucherPlugin.SettingsName) ?? new VoucherSettings();
        return View(new VoucherImageSettingsViewModel { Images = settings.Images });
    }

    [HttpPost("~/plugins/{storeId}/vouchers/images/upload")]
    [Authorize(Policy = Policies.CanModifyStoreSettings)]
    public async Task<IActionResult> UploadVoucherImage(string storeId, [FromForm] VoucherImageSettingsViewModel vm)
    {
        if (CurrentStore == null)
            return NotFound();

        var settings = await _storeRepository.GetSettingAsync<VoucherSettings>(CurrentStore.Id, VoucherPlugin.SettingsName) ?? new VoucherSettings();
        if (settings.Images.Any(c => c.Name.ToLower() == vm.NewImageTitle.Trim().ToLower()))
        {
            TempData[WellKnownTempData.ErrorMessage] = "An existing voucher image exist with that title";
            return RedirectToAction(nameof(VoucherImageSettings), new { storeId });
        }
        var validationResult = ValidateImageForPosPrinting(vm.NewImageFile);
        if (!validationResult.IsValid)
        {
            TempData[WellKnownTempData.ErrorMessage] = validationResult.ErrorMessage;
            return RedirectToAction(nameof(VoucherImageSettings), new { storeId });
        }

        var originalFile = vm.NewImageFile;
        var newFileName = $"voucher_template_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}{Path.GetExtension(originalFile.FileName)}";
        var renamedFile = new FormFile(originalFile.OpenReadStream(), 0, originalFile.Length, originalFile.Name, newFileName)
        {
            Headers = originalFile.Headers,
            ContentType = originalFile.ContentType
        };

        UploadImageResultModel imageUpload = await _fileService.UploadImage(renamedFile, GetUserId());
        if (!imageUpload.Success)
        {
            TempData[WellKnownTempData.ErrorMessage] = imageUpload.Response;
            return RedirectToAction(nameof(VoucherImageSettings), new { storeId });
        }
        var rootUri = Request.GetAbsoluteRootUri();
        var fileUrl = await _fileService.GetFileUrl(rootUri, imageUpload.StoredFile.Id);

        var imageLink = fileUrl == null
            ? null : await _uriResolver.Resolve(rootUri, new UnresolvedUri.Raw(fileUrl));

        settings.Images.Add(new VoucherImageSettings
        {
            Key = Encoders.Base58.EncodeData(RandomUtils.GetBytes(6)),
            Name = vm.NewImageTitle.Trim(),
            FileUrl = imageLink,
            StoredFileId = imageUpload.StoredFile.Id
        });
        await _storeRepository.UpdateSetting(CurrentStore.Id, VoucherPlugin.SettingsName, settings);
        TempData[WellKnownTempData.SuccessMessage] = $"Voucher image uploaded successfully";
        return RedirectToAction(nameof(VoucherImageSettings), new { storeId });
    }


    [HttpGet("~/plugins/{storeId}/vouchers/images/{imageKey}/toggle")]
    [Authorize(Policy = Policies.CanModifyStoreSettings)]
    public async Task<IActionResult> ToggleVoucherImage(string storeId, string imageKey)
    {
        if (CurrentStore is null) return NotFound();

        var settings = await _storeRepository.GetSettingAsync<VoucherSettings>(CurrentStore.Id, VoucherPlugin.SettingsName) ?? new VoucherSettings();
        if (settings?.Images == null || !settings.Images.Any())
        {
            TempData[WellKnownTempData.ErrorMessage] = "No voucher images found";
            return RedirectToAction(nameof(VoucherImageSettings), new { storeId });
        }
        var voucherImage = settings.Images.FirstOrDefault(c => c.Key == imageKey);
        if (voucherImage == null)
        {
            TempData[WellKnownTempData.ErrorMessage] = "Invalid voucher image specified";
            return RedirectToAction(nameof(VoucherImageSettings), new { storeId });
        }

        return View("Confirm",
            new ConfirmModel($"{(voucherImage.Enabled ? "Disable" : "Enable")} voucher image",
                $"The voucher image ({voucherImage.Name}) will be {(voucherImage.Enabled ? "disabled" : "enabled")}. Are you sure?", voucherImage.Enabled ? "Disable" : "Enable"));
    }

    [HttpPost("~/plugins/{storeId}/vouchers/images/{imageKey}/toggle")]
    [Authorize(Policy = Policies.CanModifyStoreSettings)]
    public async Task<IActionResult> ToggleVoucherImagePost(string storeId, string imageKey)
    {
        if (CurrentStore is null) 
            return NotFound();

        var settings = await _storeRepository.GetSettingAsync<VoucherSettings>(CurrentStore.Id, VoucherPlugin.SettingsName) ?? new VoucherSettings();
        if (settings?.Images == null || !settings.Images.Any())
        {
            TempData[WellKnownTempData.ErrorMessage] = "No voucher images found";
            return RedirectToAction(nameof(VoucherImageSettings), new { storeId });
        }
        var voucherImage = settings.Images.FirstOrDefault(c => c.Key == imageKey);
        if (voucherImage == null)
        {
            TempData[WellKnownTempData.ErrorMessage] = "Invalid voucher image specified";
            return RedirectToAction(nameof(VoucherImageSettings), new { storeId });
        }
        voucherImage.Enabled = !voucherImage.Enabled;
        await _storeRepository.UpdateSetting(CurrentStore.Id, VoucherPlugin.SettingsName, settings);
        TempData[WellKnownTempData.SuccessMessage] = $"Voucher image {(voucherImage.Enabled ? "enabled" : "disabled")} successfully";
        return RedirectToAction(nameof(VoucherImageSettings), new { storeId });
    }


    [HttpGet("~/plugins/{storeId}/vouchers/images/{imageKey}/delete")]
    [Authorize(Policy = Policies.CanModifyStoreSettings)]
    public async Task<IActionResult> Delete(string storeId, string imageKey)
    {
        if (CurrentStore == null)
            return NotFound();

        var settings = await _storeRepository.GetSettingAsync<VoucherSettings>(CurrentStore.Id, VoucherPlugin.SettingsName) ?? new VoucherSettings();
        if (settings?.Images == null || !settings.Images.Any())
        {
            TempData[WellKnownTempData.ErrorMessage] = "No voucher images found";
            return RedirectToAction(nameof(VoucherImageSettings), new { storeId });
        }
        var voucherImage = settings.Images.FirstOrDefault(c => c.Key == imageKey);
        if (voucherImage == null)
        {
            TempData[WellKnownTempData.ErrorMessage] = "Invalid voucher image specified";
            return RedirectToAction(nameof(VoucherImageSettings), new { storeId });
        }
        return View("Confirm", new ConfirmModel("Delete user", $"The voucher image: {voucherImage.Name} will be deleted. Are you sure?", "Delete"));
    }

    [HttpPost("~/plugins/{storeId}/vouchers/images/{imageKey}/delete")]
    [Authorize(Policy = Policies.CanModifyStoreSettings)]
    public async Task<IActionResult> DeletePost(string storeId, string imageKey)
    {
        if (CurrentStore == null)
            return NotFound();

        var settings = await _storeRepository.GetSettingAsync<VoucherSettings>(CurrentStore.Id, VoucherPlugin.SettingsName) ?? new VoucherSettings();
        if (settings?.Images == null || !settings.Images.Any())
        {
            TempData[WellKnownTempData.ErrorMessage] = "No voucher images found";
            return RedirectToAction(nameof(VoucherImageSettings), new { storeId });
        }

        var voucherImage = settings.Images.FirstOrDefault(c => c.Key == imageKey);
        if (voucherImage == null)
        {
            TempData[WellKnownTempData.ErrorMessage] = "Invalid voucher image specified";
            return RedirectToAction(nameof(VoucherImageSettings), new { storeId });
        }
        if (settings.SelectedVoucherImage == voucherImage.Name)
            settings.SelectedVoucherImage = null;

        settings.Images.Remove(voucherImage);
        await _storeRepository.UpdateSetting(CurrentStore.Id, VoucherPlugin.SettingsName, settings);
        TempData[WellKnownTempData.SuccessMessage] = "Voucher image deleted successfully";
        return RedirectToAction(nameof(VoucherImageSettings), new { storeId });
    }

    private IActionResult HandleImageRedirectView(VoucherSettings voucherSettings, string fallbackAction, string storeId, string pullPaymentId)
    {
        if (voucherSettings.FunModeEnabled && voucherSettings.Images.Any())
        {
            string imageKey;
            if (voucherSettings.UseRandomImage)
            {
                var enabledImages = voucherSettings.Images.Where(i => i.Enabled).ToList();
                if (!enabledImages.Any())
                {
                    TempData[WellKnownTempData.ErrorMessage] = "No enabled voucher images available";
                    return RedirectToAction(fallbackAction, new { storeId });
                }
                var randomIndex = Random.Shared.Next(enabledImages.Count);
                imageKey = enabledImages[randomIndex].Key;
            }
            else
            {
                if (string.IsNullOrEmpty(voucherSettings.SelectedVoucherImage))
                {
                    TempData[WellKnownTempData.ErrorMessage] = "No voucher image selected";
                    return RedirectToAction(fallbackAction, new { storeId });
                }
                var matchedImage = voucherSettings.Images.FirstOrDefault(c => c.Name.ToLower() == voucherSettings.SelectedVoucherImage.ToLower());
                if (matchedImage == null)
                {
                    TempData[WellKnownTempData.ErrorMessage] = "Selected voucher image not found";
                    return RedirectToAction(fallbackAction, new { storeId });
                }
                imageKey = matchedImage.Key;
            }
            return RedirectToAction(nameof(ViewPrintSatsBill), new { storeId, id = pullPaymentId, imageKey });
        }
        return RedirectToAction(nameof(View), new { id = pullPaymentId });
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

    public async Task<VoucherSettings> UploadDefaultVoucherImages(VoucherSettings settings)
    {
        if (!await _fileService.IsAvailable()) return settings;

        foreach (var (key, fileName) in VoucherImages.ImageMap)
        {
            if (settings.Images.Any(i => i.Key == key)) continue;

            await using var stream = VoucherImages.GetImageStream(fileName);
            if (stream == null) continue;

            IFormFile formFile = new FormFile(stream, 0, stream.Length, "file", fileName)
            {
                Headers = new HeaderDictionary(),
                ContentType = "image/png"
            };
            var upload = await _fileService.UploadImage(formFile, GetUserId());
            if (!upload.Success) continue;

            var rootUri = Request.GetAbsoluteRootUri();
            var fileUrl = await _fileService.GetFileUrl(rootUri, upload.StoredFile.Id);

            if (fileUrl == null) continue;

            var imageLink = await _uriResolver.Resolve(rootUri, new UnresolvedUri.Raw(fileUrl));
            settings.Images.Add(new VoucherImageSettings
            {
                Key = Encoders.Base58.EncodeData(RandomUtils.GetBytes(6)),
                Name = key,
                FileUrl = imageLink,
                StoredFileId = upload.StoredFile.Id
            });
        }
        await _storeRepository.UpdateSetting(CurrentStore.Id, VoucherPlugin.SettingsName, settings);
        return settings;
    }

    private (bool IsValid, string ErrorMessage) ValidateImageForPosPrinting(IFormFile imageFile)
    {
        const long MaxFileSize = 1024 * 1024;
        if (imageFile.Length >= MaxFileSize)
        {
            return (false, "Image file is too large. File size should be less than 1MB");
        }
        var extension = Path.GetExtension(imageFile.FileName)?.ToLowerInvariant();
        if (extension != ".png" && extension != ".jpg" && extension != ".jpeg")
        {
            return (false, "Only PNG and JPEG images are supported");
        }
        if (!imageFile.ContentType.StartsWith("image/"))
        {
            return (false, "Invalid image file");
        }
        return (true, string.Empty);
    }
}
