using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data.Models;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.RockstarDev.Plugins.Payroll;

[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class PayrollUserController : Controller
{
    private readonly ApplicationDbContextFactory _dbContextFactory;
    private readonly PayrollPluginDbContextFactory _payrollPluginDbContextFactory;

    public PayrollUserController(ApplicationDbContextFactory dbContextFactory,
        PayrollPluginDbContextFactory payrollPluginDbContextFactory,
        StoreRepository storeRepository)
    {
        _dbContextFactory = dbContextFactory;
        _payrollPluginDbContextFactory = payrollPluginDbContextFactory;
    }
    public StoreData CurrentStore => HttpContext.GetStoreData();


    [HttpGet("~/plugins/{storeId}/payroll/users")]
    public async Task<IActionResult> List(string storeId)
    {
        var now = DateTimeOffset.UtcNow;
        await using var ctx = _payrollPluginDbContextFactory.CreateContext();
        var payrollUsers = await ctx.PayrollUsers
            .Where(a => a.StoreId == storeId)
            .OrderByDescending(data => data.Name).ToListAsync();

        return View(payrollUsers.ToList());
    }

    [HttpGet("~/plugins/{storeId}/payroll/users/create")]
    public async Task<IActionResult> Create()
    {
        return View(new PayrollUserCreateViewModel());
    }

    [HttpPost("~/plugins/{storeId}/payroll/users/create")]

    public async Task<IActionResult> Create(PayrollUserCreateViewModel model)
    {
        if (CurrentStore is null)
            return NotFound();

        if (model.Password != model.ConfirmPassword)
            ModelState.AddModelError(nameof(model.ConfirmPassword), "Password fields don't match");

        // TODO: Validate that user doesn't exist in database

        if (!ModelState.IsValid)
            return View(model);

        var dbUser = new PayrollUser
        {
            Name = model.Name,
            Email = model.Email,
            Password = model.Password,
            StoreId = CurrentStore.Id
        };

        await using var ctx = _payrollPluginDbContextFactory.CreateContext();
        ctx.Add(dbUser);
        await ctx.SaveChangesAsync();

        this.TempData.SetStatusMessageModel(new StatusMessageModel()
        {
            Message = $"New user {dbUser.Name} created successfully",
            Severity = StatusMessageModel.StatusSeverity.Success
        });
        return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
    }

    public class PayrollUserCreateViewModel
    {
        public string Id { get; set; }
        [MaxLength(50)]
        [Required]

        public string Name { get; set; }

        [Required]
        [EmailAddress]
        public string Email { get; set; }
        [Required]
        public string Password { get; set; }
        public string ConfirmPassword { get; set; }

    }
}