using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using static BTCPayServer.RockstarDev.Plugins.Payroll.PayrollInvoiceController;

namespace BTCPayServer.RockstarDev.Plugins.Payroll;

[AllowAnonymous]
public class PublicController : Controller
{
    private readonly ApplicationDbContextFactory _dbContextFactory;
    private readonly PayrollPluginDbContextFactory _payrollPluginDbContextFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PublicController(ApplicationDbContextFactory dbContextFactory,
        PayrollPluginDbContextFactory payrollPluginDbContextFactory,
        IHttpContextAccessor httpContextAccessor)
    {
        _dbContextFactory = dbContextFactory;
        _payrollPluginDbContextFactory = payrollPluginDbContextFactory;
        _httpContextAccessor = httpContextAccessor;
    }


    [HttpGet("~/plugins/{storeId}/payroll/public/login")]
    public async Task<IActionResult> Login(string storeId)
    {
        var store = await loadStore(storeId);
        if (store == null)
            return NotFound();

        var model = new PublicLoginViewModel();
        model.StoreName = store.StoreName;
        model.StoreBranding = new StoreBrandingViewModel(store.GetStoreBlob());

        return View(model);
    }

    private async Task<StoreData> loadStore(string storeId)
    {
        await using var ctx = _dbContextFactory.CreateContext();
        return await ctx.Stores.SingleOrDefaultAsync(a => a.Id == storeId);
    }

    [HttpPost("~/plugins/{storeId}/payroll/public/login")]
    public async Task<IActionResult> Login(string storeId, PublicLoginViewModel model)
    {
        var store = await loadStore(storeId);
        if (store == null)
            return NotFound();

        model.StoreName = store.StoreName;
        model.StoreBranding = new StoreBrandingViewModel(store.GetStoreBlob());
        //

        await using var dbPlugins = _payrollPluginDbContextFactory.CreateContext();
        var userInDb = dbPlugins.PayrollUsers.SingleOrDefault(a =>
            a.StoreId == storeId && a.Email == model.Email && a.Password == model.Password);
        if (userInDb == null)
            ModelState.AddModelError(nameof(model.Password), "Invalid credentials");

        if (!ModelState.IsValid)
            return View(model);

        // Validate login credentials here and get user details.
        _httpContextAccessor.HttpContext.Session.SetString(PAYROLL_AUTH_USER_ID, userInDb.Id);        

        return RedirectToAction(nameof(ListInvoices), new { storeId = storeId });
    }
    public class PublicLoginViewModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; }
        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; }
        [Display(Name = "Remember me")]
        public bool RememberMe { get; set; }

        // store properties
        public string StoreName { get; set; }
        public StoreBrandingViewModel StoreBranding { get; set; }
    }

    private const string PAYROLL_AUTH_USER_ID = "PAYROLL_AUTH_USER_ID";

    //

    [HttpGet("~/plugins/{storeId}/payroll/public/logout")]
    public async Task<IActionResult> Logout(string storeId)
    {
        _httpContextAccessor.HttpContext.Session.Remove(PAYROLL_AUTH_USER_ID);
        return redirectToLogin(storeId);
    }

    private IActionResult redirectToLogin(string storeId)
    {
        return RedirectToAction(nameof(Login), new { storeId = storeId });
    }

    [HttpGet("~/plugins/{storeId}/payroll/public/listinvoices")]
    public async Task<IActionResult> ListInvoices(string storeId)
    {
        var store = await loadStore(storeId);
        if (store == null)
            return NotFound();

        var userId = _httpContextAccessor.HttpContext.Session.GetString(PAYROLL_AUTH_USER_ID);
        if (userId == null)
            return redirectToLogin(storeId);

        await using var ctx = _payrollPluginDbContextFactory.CreateContext();
        var payrollInvoices = await ctx.PayrollInvoices
            .Include(data => data.User)
            .Where(p => p.User.StoreId == storeId && p.UserId == userId && p.IsArchived == false)
            .OrderByDescending(data => data.CreatedAt).ToListAsync();

        var model = new PublicListInvoicesViewModel();
        model.StoreId = store.Id;
        model.StoreName = store.StoreName;
        model.StoreBranding = new StoreBrandingViewModel(store.GetStoreBlob());
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
            Description = tuple.Description,
            InvoiceUrl = tuple.InvoiceFilename
        }).ToList();

        return View(model);
    }
    public class PublicListInvoicesViewModel
    {
        public List<PayrollInvoiceViewModel> Invoices { get; set; }

        // store properties
        public string StoreId { get; set; }
        public string StoreName { get; set; }
        public StoreBrandingViewModel StoreBranding { get; set; }
    }
}