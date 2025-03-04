using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.RockstarDev.Plugins.Subscriptions.Data;
using BTCPayServer.RockstarDev.Plugins.Subscriptions.Data.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using BTCPayServer.Abstractions.Contracts;

namespace BTCPayServer.RockstarDev.Plugins.Subscriptions.Controllers;

[Route("~/plugins/{storeId}/subscriptions/products/")]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class ProductController : Controller
{
    private readonly SubscriptionsPluginDbContext _dbContext;
    private readonly IScopeProvider _scopeProvider;

    public ProductController(SubscriptionsPluginDbContext dbContext, IScopeProvider scopeProvider)
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

        var products = await _dbContext.Products
            .Where(p => p.StoreId == storeId)
            .ToListAsync();

        return View(products);
    }

    [HttpGet("create")]
    public IActionResult Create()
    {
        return View(new Product());
    }

    [HttpPost("create")]
    public async Task<IActionResult> Create(Product product)
    {
        if (!ModelState.IsValid)
            return View(product);

        var storeId = _scopeProvider.GetCurrentStoreId();
        if (string.IsNullOrEmpty(storeId))
            return Forbid();

        product.StoreId = storeId;
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

    [HttpGet("edit/{id}")]
    public async Task<IActionResult> Edit(string id)
    {
        var storeId = _scopeProvider.GetCurrentStoreId();
        if (string.IsNullOrEmpty(storeId))
            return Forbid();

        var product = await _dbContext.Products
            .Where(p => p.StoreId == storeId && p.Id == id)
            .FirstOrDefaultAsync();

        if (product == null)
            return NotFound();

        return View(product);
    }

    [HttpPost("edit/{id}")]
    public async Task<IActionResult> Edit(string id, Product product)
    {
        if (!ModelState.IsValid)
            return View(product);

        var storeId = _scopeProvider.GetCurrentStoreId();
        if (string.IsNullOrEmpty(storeId))
            return Forbid();

        var existingProduct = await _dbContext.Products
            .Where(p => p.StoreId == storeId && p.Id == id)
            .FirstOrDefaultAsync();

        if (existingProduct == null)
            return NotFound();

        existingProduct.Name = product.Name;
        existingProduct.Price = product.Price;
        existingProduct.ReminderDays = product.ReminderDays;

        await _dbContext.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("delete/{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var storeId = _scopeProvider.GetCurrentStoreId();
        if (string.IsNullOrEmpty(storeId))
            return Forbid();

        var product = await _dbContext.Products
            .Where(p => p.StoreId == storeId && p.Id == id)
            .FirstOrDefaultAsync();

        if (product == null)
            return NotFound();

        _dbContext.Products.Remove(product);
        await _dbContext.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}
