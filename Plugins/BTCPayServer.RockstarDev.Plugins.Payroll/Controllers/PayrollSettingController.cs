using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using BTCPayServer.RockstarDev.Plugins.Payroll.Logic;
using BTCPayServer.RockstarDev.Plugins.Payroll.ViewModels;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Controllers;

[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class PayrollSettingController : Controller
{
    private readonly PayrollPluginDbContextFactory _payrollPluginDbContextFactory;
    private readonly ISettingsRepository _settingsRepository;

    public PayrollSettingController(PayrollPluginDbContextFactory payrollPluginDbContextFactory,
        ISettingsRepository settingsRepository)
    {
        _payrollPluginDbContextFactory = payrollPluginDbContextFactory;
        _settingsRepository = settingsRepository;
    }

    private StoreData CurrentStore => HttpContext.GetStoreData();


    [HttpGet("~/plugins/{storeId}/payroll/settings")]
    public async Task<IActionResult> Settings(string storeId)
    {
        var settings = await _payrollPluginDbContextFactory.GetSettingAsync(storeId);
        var model = new PayrollPluginSettingViewModel
        {
            MakeInvoiceFileOptional = settings.MakeInvoiceFilesOptional,
            PurchaseOrdersRequired = settings.PurchaseOrdersRequired
        };
        return View(model);
    }

    [HttpPost("~/plugins/{storeId}/payroll/settings")]

    public async Task<IActionResult> Settings(string storeId, PayrollPluginSettingViewModel model)
    {
        if (CurrentStore is null)
            return NotFound();

        var settings = new PayrollStoreSetting
        {
            MakeInvoiceFilesOptional = model.MakeInvoiceFileOptional,
            PurchaseOrdersRequired = model.PurchaseOrdersRequired
        };
        
        await _payrollPluginDbContextFactory.SetSettingAsync(storeId, settings);
        return RedirectToAction(nameof(PayrollInvoiceController.List), "PayrollInvoice", new { storeId = CurrentStore.Id });

    }
}