using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.RockstarDev.Plugins.XpubExtractor.Controllers;

[Authorize(Policy = Policies.CanViewInvoices, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class XpubExtractorController : Controller
{
    [HttpGet("~/plugins/{storeId}/xpubextractor/")]
    public IActionResult Index(string storeId)
    {
        return View();
    }
}
