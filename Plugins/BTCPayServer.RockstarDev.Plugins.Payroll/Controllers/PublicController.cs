using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using System;
using System.Linq;
using System.Threading.Tasks;
using static BTCPayServer.RockstarDev.Plugins.Payroll.Controllers.PayrollInvoiceController;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Controllers;

[AllowAnonymous]
public class PublicController : Controller
{
    private readonly ApplicationDbContextFactory _dbContextFactory;
    private readonly PayrollPluginDbContextFactory _payrollPluginDbContextFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly BTCPayNetworkProvider _networkProvider;
    private readonly IFileService _fileService;
    private readonly PayrollPluginPassHasher _hasher;
    private readonly ISettingsRepository _settingsRepository;

    public PublicController(ApplicationDbContextFactory dbContextFactory,
        PayrollPluginDbContextFactory payrollPluginDbContextFactory,
        IHttpContextAccessor httpContextAccessor,
        BTCPayNetworkProvider networkProvider,
        IFileService fileService,
        PayrollPluginPassHasher hasher,
        ISettingsRepository settingsRepository)
    {
        _dbContextFactory = dbContextFactory;
        _payrollPluginDbContextFactory = payrollPluginDbContextFactory;
        _httpContextAccessor = httpContextAccessor;
        _networkProvider = networkProvider;
        _fileService = fileService;
        _hasher = hasher;
        _settingsRepository = settingsRepository;
    }

    private const string PAYROLL_AUTH_USER_ID = "PAYROLL_AUTH_USER_ID";


    [HttpGet("~/plugins/{storeId}/payroll/public/login")]
    public async Task<IActionResult> Login(string storeId)
    {
        var vali = await validateStoreAndUser(storeId, false);
        if (vali.ErrorActionResult != null)
            return vali.ErrorActionResult;

        var model = new PublicLoginViewModel
        {
            StoreName = vali.Store.StoreName,
            StoreBranding = new StoreBrandingViewModel(vali.Store.GetStoreBlob())
        };

        return View(model);
    }

    [HttpPost("~/plugins/{storeId}/payroll/public/login")]
    public async Task<IActionResult> Login(string storeId, PublicLoginViewModel model)
    {
        var vali = await validateStoreAndUser(storeId, false);
        if (vali.ErrorActionResult != null)
            return vali.ErrorActionResult;

        model.StoreId = vali.Store.Id;
        model.StoreName = vali.Store.StoreName;
        model.StoreBranding = new StoreBrandingViewModel(vali.Store.GetStoreBlob());

        await using var dbPlugins = _payrollPluginDbContextFactory.CreateContext();
        var userInDb = dbPlugins.PayrollUsers.SingleOrDefault(a =>
            a.StoreId == storeId && a.Email == model.Email.ToLowerInvariant());

        if (userInDb != null)
        {
            if (userInDb.State == PayrollUserState.Active && _hasher.IsValidPassword(userInDb, model.Password))
            {
                _httpContextAccessor.HttpContext!.Session.SetString(PAYROLL_AUTH_USER_ID, userInDb!.Id);
                return RedirectToAction(nameof(ListInvoices), new { storeId });
            }
        }

        // if we end up here, credentials are invalid 
        ModelState.AddModelError(nameof(model.Password), "Invalid credentials");
        return View(model);
    }

    //

    [HttpGet("~/plugins/{storeId}/payroll/public/logout")]
    public IActionResult Logout(string storeId)
    {
        _httpContextAccessor.HttpContext?.Session.Remove(PAYROLL_AUTH_USER_ID);
        return redirectToLogin(storeId);
    }

    private IActionResult redirectToLogin(string storeId)
    {
        return RedirectToAction(nameof(Login), new { storeId });
    }

    [HttpGet("~/plugins/{storeId}/payroll/public/listinvoices")]
    public async Task<IActionResult> ListInvoices(string storeId)
    {
        var vali = await validateStoreAndUser(storeId, true);
        if (vali.ErrorActionResult != null)
            return vali.ErrorActionResult;

        await using var ctx = _payrollPluginDbContextFactory.CreateContext();
        var payrollInvoices = await ctx.PayrollInvoices
            .Include(data => data.User)
            .Where(p => p.User.StoreId == storeId && p.UserId == vali.UserId && p.IsArchived == false)
            .OrderByDescending(data => data.CreatedAt).ToListAsync();

        var model = new PublicListInvoicesViewModel();
        model.StoreId = vali.Store.Id;
        model.StoreName = vali.Store.StoreName;
        model.StoreBranding = new StoreBrandingViewModel(vali.Store.GetStoreBlob());
        model.Invoices = payrollInvoices.Select(tuple => new PayrollInvoiceViewModel()
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
        }).ToList();

        return View(model);
    }

    private async Task<StoreUserValidator> validateStoreAndUser(string storeId, bool validateUser)
    {
        await using var dbMain = _dbContextFactory.CreateContext();
        var store = await dbMain.Stores.SingleOrDefaultAsync(a => a.Id == storeId);
        if (store == null)
            return new StoreUserValidator { ErrorActionResult = NotFound() };

        string userId = null;
        if (validateUser)
        {
            await using var dbPlugin = _payrollPluginDbContextFactory.CreateContext();
            userId = _httpContextAccessor.HttpContext!.Session.GetString(PAYROLL_AUTH_USER_ID);
            var userInDb = dbPlugin.PayrollUsers.SingleOrDefault(a =>
                a.StoreId == storeId && a.Id == userId && a.State == PayrollUserState.Active);
            if (userInDb == null)
                return new StoreUserValidator { ErrorActionResult = redirectToLogin(storeId) };
            else
                userId = userInDb.Id;
        }

        return new StoreUserValidator { Store = store, UserId = userId };
    }
    private class StoreUserValidator
    {
        public IActionResult ErrorActionResult { get; set; }
        public StoreData Store { get; set; }
        public string UserId { get; set; }
    }


    // upload
    [HttpGet("~/plugins/{storeId}/payroll/public/upload")]
    public async Task<IActionResult> Upload(string storeId)
    {
        var vali = await validateStoreAndUser(storeId, true);
        if (vali.ErrorActionResult != null)
            return vali.ErrorActionResult;

        var model = new PublicPayrollInvoiceUploadViewModel();
        model.StoreId = vali.Store.Id;
        model.StoreName = vali.Store.StoreName;
        model.StoreBranding = new StoreBrandingViewModel(vali.Store.GetStoreBlob());

        model.Amount = 0;
        model.Currency = vali.Store.GetStoreBlob().DefaultCurrency;

        return View(model);
    }

    [HttpPost("~/plugins/{storeId}/payroll/public/upload")]

    public async Task<IActionResult> Upload(string storeId, PublicPayrollInvoiceUploadViewModel model)
    {
        var vali = await validateStoreAndUser(storeId, true);
        if (vali.ErrorActionResult != null)
            return vali.ErrorActionResult;

        model.StoreId = vali.Store.Id;
        model.StoreName = vali.Store.StoreName;
        model.StoreBranding = new StoreBrandingViewModel(vali.Store.GetStoreBlob());

        if (model.Amount <= 0)
            ModelState.AddModelError(nameof(model.Amount), "Amount must be more than 0.");

        try
        {
            var network = _networkProvider.GetNetwork<BTCPayNetwork>(PayrollPluginConst.BTC_CRYPTOCODE);
            var unused = Network.Parse<BitcoinAddress>(model.Destination, network.NBitcoinNetwork);
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
            UserId = vali.UserId,
            State = PayrollInvoiceState.AwaitingApproval
        };

        dbPlugin.Add(dbPayrollInvoice);
        await dbPlugin.SaveChangesAsync();

        TempData.SetStatusMessageModel(new StatusMessageModel()
        {
            Message = $"Invoice uploaded successfully",
            Severity = StatusMessageModel.StatusSeverity.Success
        });
        return RedirectToAction(nameof(ListInvoices), new { storeId });
    }


    // change password

    [HttpGet("~/plugins/{storeId}/payroll/public/changepassword")]
    public async Task<IActionResult> ChangePassword(string storeId)
    {
        var vali = await validateStoreAndUser(storeId, true);
        if (vali.ErrorActionResult != null)
            return vali.ErrorActionResult;

        var model = new PublicChangePasswordViewModel();
        model.StoreId = vali.Store.Id;
        model.StoreName = vali.Store.StoreName;
        model.StoreBranding = new StoreBrandingViewModel(vali.Store.GetStoreBlob());

        return View(model);
    }

    [HttpPost("~/plugins/{storeId}/payroll/public/changepassword")]
    public async Task<IActionResult> ChangePassword(string storeId, PublicChangePasswordViewModel model)
    {
        var vali = await validateStoreAndUser(storeId, true);
        if (vali.ErrorActionResult != null)
            return vali.ErrorActionResult;

        model.StoreId = vali.Store.Id;
        model.StoreName = vali.Store.StoreName;
        model.StoreBranding = new StoreBrandingViewModel(vali.Store.GetStoreBlob());

        if (!ModelState.IsValid)
            return View(model);

        await using var dbPlugins = _payrollPluginDbContextFactory.CreateContext();
        var userInDb = dbPlugins.PayrollUsers.SingleOrDefault(a =>
            a.StoreId == storeId && a.Id == vali.UserId);
        if (userInDb == null)
            ModelState.AddModelError(nameof(model.CurrentPassword), "Invalid password");

        if (!_hasher.IsValidPassword(userInDb, model.CurrentPassword))
            ModelState.AddModelError(nameof(model.CurrentPassword), "Invalid password");

        if (!ModelState.IsValid)
            return View(model);



        // 
        userInDb!.Password = _hasher.HashPassword(vali.UserId, model.NewPassword);
        await dbPlugins.SaveChangesAsync();

        //
        TempData.SetStatusMessageModel(new StatusMessageModel()
        {
            Message = $"Password successfully changed",
            Severity = StatusMessageModel.StatusSeverity.Success
        });
        return RedirectToAction(nameof(ListInvoices), new { storeId });
    }
}