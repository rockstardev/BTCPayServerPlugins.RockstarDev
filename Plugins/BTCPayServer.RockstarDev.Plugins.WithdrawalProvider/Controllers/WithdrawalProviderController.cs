using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.RockstarDev.Plugins.WithdrawalProvider.Services;
using BTCPayServer.RockstarDev.Plugins.WithdrawalProvider.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.RockstarDev.Plugins.WithdrawalProvider.Controllers;

[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Route("plugins/{storeId}/withdrawal-provider")]
public class WithdrawalProviderController(WithdrawalProviderService service) : Controller
{
    [HttpGet("")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> Index(string storeId)
    {
        var vm = await BuildViewModel(storeId);
        return View(vm);
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> Save(string storeId, WithdrawalProviderViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            var hydrated = await BuildViewModel(storeId, vm);
            return View("Index", hydrated);
        }

        var settings = new WithdrawalProviderSettings
        {
            Enabled = vm.Enabled,
            ApiKey = vm.ApiKey,
            Ticker = vm.Ticker.Trim().ToUpperInvariant(),
            FiatCurrency = vm.FiatCurrency.Trim().ToUpperInvariant(),
            PaymentMethod = vm.PaymentMethod.Trim().ToUpperInvariant()
        };

        await service.SaveSettings(storeId, settings);
        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Message = "Withdrawal Provider settings saved.",
            Severity = StatusMessageModel.StatusSeverity.Success
        });

        return RedirectToAction(nameof(Index), new { storeId });
    }

    [HttpPost("test-api-key")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> TestApiKey(string storeId)
    {
        var settings = await service.GetSettings(storeId);
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = "Please save an API key first.",
                Severity = StatusMessageModel.StatusSeverity.Error
            });
            return RedirectToAction(nameof(Index), new { storeId });
        }

        try
        {
            var userId = await service.TestApiKey(settings.ApiKey);
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = $"API key is valid. User ID: {userId}",
                Severity = StatusMessageModel.StatusSeverity.Success
            });
        }
        catch (Exception ex)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = $"API key validation failed: {ex.Message}",
                Severity = StatusMessageModel.StatusSeverity.Error
            });
        }

        return RedirectToAction(nameof(Index), new { storeId });
    }

    [HttpPost("refresh")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> Refresh(string storeId)
    {
        var vm = await BuildViewModel(storeId);
        return View("Index", vm);
    }

    [HttpPost("create-order")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> CreateOrder(string storeId, long sourceAmountSats, string ipAddress, string paymentMethod)
    {
        try
        {
            var order = await service.CreateOrder(storeId, sourceAmountSats, ipAddress, paymentMethod);
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = $"Order created: {order.Id}",
                Severity = StatusMessageModel.StatusSeverity.Success
            });

            var vm = await BuildViewModel(storeId);
            vm.LastOrderId = order.Id;
            vm.LastOrderDestination = !string.IsNullOrWhiteSpace(order.Invoice)
                ? order.Invoice
                : order.DepositAddress;
            vm.SourceAmountSats = sourceAmountSats;
            vm.IpAddress = ipAddress;
            vm.PaymentMethod = paymentMethod;

            return View("Index", vm);
        }
        catch (Exception ex)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = $"Order creation failed: {ex.Message}",
                Severity = StatusMessageModel.StatusSeverity.Error
            });
            var vm = await BuildViewModel(storeId);
            vm.SourceAmountSats = sourceAmountSats;
            vm.IpAddress = ipAddress;
            vm.PaymentMethod = paymentMethod;
            return View("Index", vm);
        }
    }

    private async Task<WithdrawalProviderViewModel> BuildViewModel(string storeId, WithdrawalProviderViewModel? current = null)
    {
        var settings = await service.GetSettings(storeId);

        var vm = current ?? new WithdrawalProviderViewModel();
        vm.StoreId = storeId;
        vm.Enabled = settings.Enabled;
        vm.ApiKey = settings.ApiKey;
        vm.Ticker = settings.Ticker;
        vm.FiatCurrency = settings.FiatCurrency;
        vm.PaymentMethod = string.IsNullOrWhiteSpace(vm.PaymentMethod) ? settings.PaymentMethod : vm.PaymentMethod;

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return vm;
        }

        try
        {
            var snapshot = await service.GetDashboardSnapshot(storeId);
            vm.UserId = snapshot.UserId;
            vm.Balance = snapshot.Balance.Balance;
            vm.Rate = snapshot.Rate;
            vm.Transactions = snapshot.Transactions.Transactions.Take(20).ToArray();
        }
        catch
        {
            // Keep page interactive even if provider request fails.
        }

        return vm;
    }
}
