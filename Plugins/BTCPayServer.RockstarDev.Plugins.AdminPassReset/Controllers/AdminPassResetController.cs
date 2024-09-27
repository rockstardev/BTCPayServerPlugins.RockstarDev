using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BTCPayServer.RockstarDev.Plugins.AdminPassReset.Controllers;

[Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class AdminPassResetController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly LinkGenerator _generator;

    public AdminPassResetController(UserManager<ApplicationUser> userManager, LinkGenerator generator)
    {
        _userManager = userManager;
        _generator = generator;
    }

    [HttpGet("~/plugins/adminpassreset/create")]
    public IActionResult Create()
    {
        return View(new AdminPassResetViewModel());
    }

    [HttpPost("~/plugins/adminpassreset/create")]
    public async Task<IActionResult> Create(AdminPassResetViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }
        
        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user == null)
        {
            ModelState.AddModelError(nameof(model.Email), "User doesn't exist");
            return View(model);
        }

        var uri = Request.GetAbsoluteRootUri();
        var host = new HostString(uri.Host, uri.Port);
        var code = await _userManager.GeneratePasswordResetTokenAsync(user!);
        model.CallbackUrl = _generator.ResetPasswordCallbackLink(user.Id, code, uri.Scheme, host, uri.PathAndQuery);
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