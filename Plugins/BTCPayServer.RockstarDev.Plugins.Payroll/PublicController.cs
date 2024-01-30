using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.RockstarDev.Plugins.Payroll;

[AllowAnonymous]
public class PublicController : Controller
{
    private readonly ApplicationDbContextFactory _dbContextFactory;
    private readonly PayrollPluginDbContextFactory _payrollPluginDbContextFactory;

    public PublicController(ApplicationDbContextFactory dbContextFactory,
        PayrollPluginDbContextFactory payrollPluginDbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
        _payrollPluginDbContextFactory = payrollPluginDbContextFactory;
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
        var userExists = dbPlugins.PayrollUsers.Any(a=>
            a.StoreId == storeId && a.Email == model.Email && a.Password == model.Password);
        if (!userExists)
            ModelState.AddModelError(nameof(model.Password), "Invalid credentials");

        if (!ModelState.IsValid)
            return View(model);

        // TODO: Redirect to list of invoices page + upload
        return View(model);
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
}