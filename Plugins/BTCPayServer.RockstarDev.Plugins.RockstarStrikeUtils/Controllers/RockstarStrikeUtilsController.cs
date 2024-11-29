using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Strike.Client;

namespace BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Controllers;

[Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class RockstarStrikeUtilsController(UserManager<ApplicationUser> userManager, StrikeClient strikeClient) : Controller
{
    [HttpGet("~/plugins/rockstarstrikeutils/ReceiveRequests")]
    public IActionResult ReceiveRequests()
    {
        return View(new AdminPassResetViewModel());
    }

    [HttpPost("~/plugins/adminpassreset/create")]
    public async Task<IActionResult> Create(AdminPassResetViewModel model)
    {
        var receiveRequests = strikeClient.ReceiveRequests.GetRequests();
        
        return View(model);
    }

    public class AdminPassResetViewModel
    {
        [Required]
        [EmailAddress]
        [Display(Name = "Email address of the user")]
        public string Email { get; set; }

        public string CallbackUrl { get; set; }
    }
}