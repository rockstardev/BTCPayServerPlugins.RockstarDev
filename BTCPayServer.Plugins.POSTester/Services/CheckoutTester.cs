using System.Text.RegularExpressions;
using BTCPayServer.Plugins.POSTester.Configuration;
using BTCPayServer.Plugins.POSTester.Models;
using Microsoft.Playwright;

namespace BTCPayServer.Plugins.POSTester.Services;

public class CheckoutTester : IDisposable
{
    private readonly TestConfiguration _config;
    private readonly BTCPayApiService _apiService;

    public CheckoutTester(TestConfiguration config)
    {
        _config = config;
        _apiService = new BTCPayApiService(config);
    }

    public async Task<PaymentResult> RunTestAsync()
    {
        var result = new PaymentResult();
        var totalStart = DateTime.UtcNow;

        try
        {
            Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] Starting POS checkout test");
            Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] Target URL: {_config.CheckoutUrl}");
            Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] Amount: {_config.Amount:F2}");

            IPlaywright? playwright = null;
            IBrowser? browser = null;
            IPage? page = null;

            try
            {
                // Initialize Playwright
                using var timer1 = new PerformanceTimer("Browser Initialization", (name, ms) => result.TimingResults[name] = ms);
                playwright = await Playwright.CreateAsync();
                browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = _config.Headless,
                    SlowMo = _config.SlowMo
                });
                page = await browser.NewPageAsync();
                timer1.Dispose();

                // Navigate to checkout
                using var timer2 = new PerformanceTimer("Page Load", (name, ms) => result.TimingResults[name] = ms);
                await page.GotoAsync(_config.CheckoutUrl);
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                timer2.Dispose();

                // Enter amount and charge
                using var timer3 = new PerformanceTimer("Amount Entry & Charge", (name, ms) => result.TimingResults[name] = ms);
                
                // Look for amount input field (common selectors for POS)
                var amountSelector = await FindAmountInputAsync(page);
                if (amountSelector == null)
                {
                    throw new Exception("Could not find amount input field");
                }

                await page.FillAsync(amountSelector, _config.Amount.ToString("F2"));
                Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] Entered amount: {_config.Amount:F2}");

                // Find and click charge button
                var chargeSelector = await FindChargeButtonAsync(page);
                if (chargeSelector == null)
                {
                    throw new Exception("Could not find charge button");
                }

                await page.ClickAsync(chargeSelector);
                Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] Clicked charge button");

                // Wait for checkout page to load
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions 
                { 
                    Timeout = _config.TimeoutSeconds * 1000 
                });
                timer3.Dispose();

                // Extract lightning invoice
                string? invoice;
                using var timer4 = new PerformanceTimer("Invoice Extraction", (name, ms) => result.TimingResults[name] = ms);
                invoice = await ExtractLightningInvoiceAsync(page);
                if (string.IsNullOrEmpty(invoice))
                {
                    throw new Exception("Could not extract lightning invoice from checkout page");
                }
                Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] Extracted invoice: {invoice[..50]}...");
                result.Invoice = invoice;
                timer4.Dispose();

                if (_config.ExtractOnly)
                {
                    result.Success = true;
                    result.Message = "Invoice extracted successfully";
                    Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] Test completed successfully!");
                    Console.WriteLine();
                    Console.WriteLine("=== LIGHTNING INVOICE EXTRACTED ===");
                    Console.WriteLine($"Invoice: {invoice}");
                    Console.WriteLine();
                    Console.WriteLine("You can now:");
                    Console.WriteLine("1. Scan the QR code on the checkout page to pay with your lightning wallet");
                    Console.WriteLine("2. Copy the above invoice and pay it manually");
                    Console.WriteLine("3. Use this invoice for testing purposes");
                    Console.WriteLine();
                }
                else
                {
                    // Pay invoice via API
                    string? paymentId;
                    using var timer5 = new PerformanceTimer("API Payment", (name, ms) => result.TimingResults[name] = ms);
                    var (paySuccess, payId, payError) = await _apiService.PayInvoiceAsync(invoice);
                    if (!paySuccess)
                    {
                        throw new Exception($"Payment failed: {payError}");
                    }
                    paymentId = payId;
                    result.PaymentId = paymentId;
                    timer5.Dispose();

                    // Wait for payment confirmation
                    using var timer6 = new PerformanceTimer("Payment Confirmation", (name, ms) => result.TimingResults[name] = ms);
                    await WaitForPaymentConfirmationAsync(page, paymentId);
                    timer6.Dispose();

                    result.Success = true;
                    result.Message = "Payment completed successfully";
                    Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] Test completed successfully!");
                }
            }
            finally
            {
                // Cleanup
                if (page != null) await page.CloseAsync();
                if (browser != null) await browser.CloseAsync();
                playwright?.Dispose();
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = ex.Message;
            Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] Test failed: {ex.Message}");
        }

        result.TotalTimeMs = (DateTime.UtcNow - totalStart).TotalMilliseconds;
        
        // Print timing summary
        Console.WriteLine($"\n=== TIMING SUMMARY ===");
        foreach (var timing in result.TimingResults)
        {
            Console.WriteLine($"{timing.Key}: {timing.Value:F2}ms");
        }
        Console.WriteLine($"Total Time: {result.TotalTimeMs:F2}ms");
        Console.WriteLine($"Status: {(result.Success ? "SUCCESS" : "FAILED")}");
        if (!result.Success && !string.IsNullOrEmpty(result.Message))
        {
            Console.WriteLine($"Error: {result.Message}");
        }

        return result;
    }

    private async Task<string?> FindAmountInputAsync(IPage page)
    {
        // Try common selectors for amount input in POS systems
        var selectors = new[]
        {
            "input[name='amount']",
            "input[id='amount']",
            "input[placeholder*='amount']",
            ".amount input",
            "#Amount",
            "input[type='number']"
        };

        foreach (var selector in selectors)
        {
            try
            {
                var element = page.Locator(selector);
                if (await element.CountAsync() > 0 && await element.First.IsVisibleAsync())
                {
                    Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] Found amount input: {selector}");
                    return selector;
                }
            }
            catch
            {
                // Continue to next selector
            }
        }

        return null;
    }

    private async Task<string?> FindChargeButtonAsync(IPage page)
    {
        // Try common selectors for charge button
        var selectors = new[]
        {
            "button:has-text('Charge')",
            "input[value='Charge']",
            "button:has-text('Pay')",
            "input[value='Pay']",
            ".charge-btn",
            "#charge",
            "button[type='submit']"
        };

        foreach (var selector in selectors)
        {
            try
            {
                var element = page.Locator(selector);
                if (await element.CountAsync() > 0 && await element.First.IsVisibleAsync())
                {
                    Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] Found charge button: {selector}");
                    return selector;
                }
            }
            catch
            {
                // Continue
            }
        }

        return null;
    }

    private async Task<string?> ExtractLightningInvoiceAsync(IPage page)
    {
        // Wait for Lightning tab or invoice to appear
        await Task.Delay(2000);

        // Try to find Lightning tab first
        try
        {
            var lightningTab = page.Locator("a[href*='Lightning'], button:has-text('Lightning'), .lightning");
            if (await lightningTab.CountAsync() > 0)
            {
                await lightningTab.First.ClickAsync();
                Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] Clicked Lightning tab");
                await Task.Delay(1000);
            }
        }
        catch
        {
            // Lightning tab might not exist or already be selected
        }

        // Look for the invoice in various possible locations
        var selectors = new[]
        {
            "#Lightning_BTC-LN span[data-text]",
            ".lightning-invoice",
            "[data-text^='lnbc']",
            "span:has-text('lnbc')",
            "div:has-text('lnbc')",
            "textarea:has-text('lnbc')",
            "input[value^='lnbc']"
        };

        foreach (var selector in selectors)
        {
            try
            {
                var element = page.Locator(selector);
                if (await element.CountAsync() > 0)
                {
                    var text = await element.First.GetAttributeAsync("data-text") ?? 
                              await element.First.InputValueAsync() ?? 
                              await element.First.TextContentAsync();
                    
                    if (!string.IsNullOrEmpty(text) && text.StartsWith("lnbc"))
                    {
                        Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] Found invoice using selector: {selector}");
                        return text.Trim();
                    }
                }
            }
            catch
            {
                // Continue to next selector
            }
        }

        // Last resort: search page content for lightning invoice pattern
        var pageContent = await page.ContentAsync();
        var invoiceMatch = Regex.Match(pageContent, @"lnbc[a-zA-Z0-9]+", RegexOptions.IgnoreCase);
        if (invoiceMatch.Success)
        {
            Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] Found invoice via regex search");
            return invoiceMatch.Value;
        }

        return null;
    }

    private async Task WaitForPaymentConfirmationAsync(IPage page, string? paymentId)
    {
        var timeout = TimeSpan.FromSeconds(_config.TimeoutSeconds);
        var start = DateTime.UtcNow;
        
        Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] Waiting for payment confirmation...");

        while (DateTime.UtcNow - start < timeout)
        {
            // Check if page shows payment success
            var successSelectors = new[]
            {
                ":has-text('Payment Received')",
                ":has-text('Paid')",
                ":has-text('Success')",
                ":has-text('Confirmed')",
                ".payment-success",
                ".success"
            };

            foreach (var selector in successSelectors)
            {
                try
                {
                    var element = page.Locator(selector);
                    if (await element.CountAsync() > 0 && await element.First.IsVisibleAsync())
                    {
                        Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] Payment confirmed on page!");
                        return;
                    }
                }
                catch
                {
                    // Continue
                }
            }

            // Also check payment status via API if we have a payment ID
            if (!string.IsNullOrEmpty(paymentId))
            {
                var (success, status, error) = await _apiService.GetPaymentStatusAsync(paymentId);
                if (success && (status.Equals("complete", StringComparison.OrdinalIgnoreCase) || 
                               status.Equals("settled", StringComparison.OrdinalIgnoreCase)))
                {
                    Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] Payment confirmed via API! Status: {status}");
                    return;
                }
            }

            await Task.Delay(1000);
        }

        throw new TimeoutException($"Payment confirmation timeout after {_config.TimeoutSeconds} seconds");
    }

    public void Dispose()
    {
        _apiService?.Dispose();
    }
}
