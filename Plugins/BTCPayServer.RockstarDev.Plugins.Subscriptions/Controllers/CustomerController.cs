using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using BTCPayServer.RockstarDev.Plugins.Subscriptions.Data;
using BTCPayServer.RockstarDev.Plugins.Subscriptions.Data.Models;
using BTCPayServer.Abstractions.Contracts;

namespace BTCPayServer.RockstarDev.Plugins.Subscriptions.Controllers;

[Route("~/plugins/{storeId}/subscriptions/customers/")]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class CustomerController : Controller
{
    private readonly PluginDbContext _dbContext;
    private readonly IScopeProvider _scopeProvider;

    public CustomerController(PluginDbContext dbContext, IScopeProvider scopeProvider)
    {
        _dbContext = dbContext;
        _scopeProvider = scopeProvider;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var storeId = _scopeProvider.GetCurrentStoreId();
        if (string.IsNullOrEmpty(storeId))
            return Forbid();

        var customers = await _dbContext.Customers
            .Where(c => c.StoreId == storeId)
            .ToListAsync();

        return View(customers);
    }

    [HttpGet("create")]
    public IActionResult Create()
    {
        return View(new Customer());
    }

    [HttpPost("create")]
    public async Task<IActionResult> Create(Customer customer)
    {
        if (!ModelState.IsValid)
            return View(customer);

        var storeId = _scopeProvider.GetCurrentStoreId();
        if (string.IsNullOrEmpty(storeId))
            return Forbid();

        customer.StoreId = storeId;
        _dbContext.Customers.Add(customer);
        await _dbContext.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }


    [HttpGet("edit/{id}")]
    public async Task<IActionResult> Edit(string id)
    {
        var storeId = _scopeProvider.GetCurrentStoreId();
        if (string.IsNullOrEmpty(storeId))
            return Forbid();

        var customer = await _dbContext.Customers
            .Where(c => c.StoreId == storeId && c.Id == id)
            .FirstOrDefaultAsync();

        if (customer == null)
            return NotFound();

        return View(customer);
    }

    [HttpPost("edit/{id}")]
    public async Task<IActionResult> Edit(string id, Customer customer)
    {
        if (!ModelState.IsValid)
            return View(customer);

        var storeId = _scopeProvider.GetCurrentStoreId();
        if (string.IsNullOrEmpty(storeId))
            return Forbid();

        var existingCustomer = await _dbContext.Customers
            .Where(c => c.StoreId == storeId && c.Id == id)
            .FirstOrDefaultAsync();

        if (existingCustomer == null)
            return NotFound();

        existingCustomer.Name = customer.Name;
        existingCustomer.Email = customer.Email;
        existingCustomer.Address1 = customer.Address1;
        existingCustomer.Address2 = customer.Address2;
        existingCustomer.City = customer.City;
        existingCustomer.Country = customer.Country;
        existingCustomer.ZipCode = customer.ZipCode;

        await _dbContext.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("delete/{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var storeId = _scopeProvider.GetCurrentStoreId();
        if (string.IsNullOrEmpty(storeId))
            return Forbid();

        var customer = await _dbContext.Customers
            .Where(c => c.StoreId == storeId && c.Id == id)
            .FirstOrDefaultAsync();

        if (customer == null)
            return NotFound();

        _dbContext.Customers.Remove(customer);
        await _dbContext.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}
