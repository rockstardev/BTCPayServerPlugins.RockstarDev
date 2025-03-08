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

[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Route("~/plugins/{storeId}/subscriptions/products/")]
public class ProductController : Controller
{
    private readonly PluginDbContext _dbContext;

    public ProductController(PluginDbContext dbContext)
    {
        _dbContext = dbContext;
    }
    
    [FromRoute] public string StoreId { get; set; }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var products = await _dbContext.Products
            .Where(p => p.StoreId == StoreId)
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
        ModelState.Remove(nameof(product.Id));
        if (!ModelState.IsValid)
            return View(product);

        product.StoreId = StoreId;
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync();
        
        TempData.SetStatusMessageModel(new StatusMessageModel()
        {
            Severity = StatusMessageModel.StatusSeverity.Success,
            Message = $"Product {product.Name} successfully created"
        });

        return RedirectToAction(nameof(Index), new {StoreId});
    }

    [HttpGet("edit/{id}")]
    public async Task<IActionResult> Edit(string id)
    {
        var product = await _dbContext.Products
            .Where(p => p.StoreId == StoreId && p.Id == id)
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

        var existingProduct = await _dbContext.Products
            .Where(p => p.StoreId == StoreId && p.Id == id)
            .FirstOrDefaultAsync();

        if (existingProduct == null)
            return NotFound();

        existingProduct.Name = product.Name;
        existingProduct.Price = product.Price;
        existingProduct.ReminderDays = product.ReminderDays;

        await _dbContext.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("delete/{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var product = await _dbContext.Products
            .Where(p => p.StoreId == StoreId && p.Id == id)
            .FirstOrDefaultAsync();
        
        return View("Confirm", new ConfirmModel($"Delete Product", 
            $"Do you really want to delete '{product.Name}' product?", "Delete"));
    }
    [HttpPost("delete/{id}")]
    public async Task<IActionResult> DeletePost(string id)
    {
        var product = await _dbContext.Products
            .Where(p => p.StoreId == StoreId && p.Id == id)
            .FirstOrDefaultAsync();

        if (product == null)
            return NotFound();

        _dbContext.Products.Remove(product);
        await _dbContext.SaveChangesAsync();
        return RedirectToAction(nameof(Index), new {StoreId});;
    }
}
