using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.RockstarDev.Plugins.CashCheckoutMethod.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.RockstarDev.Plugins.CashCheckoutMethod.Controllers;

[Route("stores/{storeId}/cash")]
[Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class StripeController() : Controller
{
    private StoreData StoreData => HttpContext.GetStoreData();
    
    [HttpGet]
    public async Task<IActionResult> StoreConfig()
    {
        var excludeFilters = StoreData.GetStoreBlob().GetExcludedPaymentMethods();
        var cashMethod = new CashCheckoutConfigurationItem();
        
        var model = new CashStoreViewModel
        {
            Enabled = !excludeFilters.Match(cashMethod.GetPaymentMethodId())
        };
        
        return View(model);
    }
}