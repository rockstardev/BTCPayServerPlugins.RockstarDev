using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.RockstarDev.Plugins.Subscriptions.Data;
using BTCPayServer.RockstarDev.Plugins.Subscriptions.Data.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.RockstarDev.Plugins.Subscriptions.Controllers;

[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Route("~/plugins/{storeId}/subscriptions/customers/")]
public class CustomerController : Controller
{
    private readonly PluginDbContext _dbContext;

    public CustomerController(PluginDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [FromRoute] public string StoreId { get; set; }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var customers = await _dbContext.Customers
            .Where(c => c.StoreId == StoreId)
            .OrderBy(a => a.Email)
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

        customer.StoreId = StoreId;
        _dbContext.Customers.Add(customer);
        await _dbContext.SaveChangesAsync();

        return RedirectToAction(nameof(Index), new { StoreId });
    }


    [HttpGet("edit/{id}")]
    public async Task<IActionResult> Edit(string id)
    {
        var customer = await _dbContext.Customers
            .Where(c => c.StoreId == StoreId && c.Id == id)
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

        var existingCustomer = await _dbContext.Customers
            .Where(c => c.StoreId == StoreId && c.Id == id)
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
        return RedirectToAction(nameof(Index), new { StoreId });
    }

    [HttpPost("delete/{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var customer = await _dbContext.Customers
            .Where(c => c.StoreId == StoreId && c.Id == id)
            .FirstOrDefaultAsync();

        if (customer == null)
            return NotFound();

        _dbContext.Customers.Remove(customer);
        await _dbContext.SaveChangesAsync();
        return RedirectToAction(nameof(Index), new { StoreId });
    }
}