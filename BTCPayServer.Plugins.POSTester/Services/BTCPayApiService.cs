using System.Text;
using System.Text.Json;
using BTCPayServer.Plugins.POSTester.Configuration;

namespace BTCPayServer.Plugins.POSTester.Services;

public class BTCPayApiService
{
    private readonly HttpClient _httpClient;
    private readonly TestConfiguration _config;

    public BTCPayApiService(TestConfiguration config)
    {
        _config = config;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"token {config.ApiKey}");
    }

    public async Task<(bool Success, string? PaymentId, string? Error)> PayInvoiceAsync(string invoice)
    {
        try
        {
            var requestBody = new
            {
                destination = invoice
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var url = $"{_config.BTCPayServerUrl.TrimEnd('/')}/api/v1/stores/{_config.StoreId}/lightning/BTC/payments";
            
            Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] Sending payment request to: {url}");
            Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] Invoice: {invoice[..50]}...");

            var response = await _httpClient.PostAsync(url, content);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<JsonElement>(responseContent);
                var paymentId = result.GetProperty("id").GetString();
                Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] Payment initiated successfully. ID: {paymentId}");
                return (true, paymentId, null);
            }

            Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] Payment failed. Status: {response.StatusCode}, Response: {responseContent}");
            return (false, null, $"HTTP {response.StatusCode}: {responseContent}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] Payment exception: {ex.Message}");
            return (false, null, ex.Message);
        }
    }

    public async Task<(bool Success, string Status, string? Error)> GetPaymentStatusAsync(string paymentId)
    {
        try
        {
            var url = $"{_config.BTCPayServerUrl.TrimEnd('/')}/api/v1/stores/{_config.StoreId}/lightning/BTC/payments/{paymentId}";
            
            var response = await _httpClient.GetAsync(url);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<JsonElement>(responseContent);
                var status = result.GetProperty("status").GetString() ?? "unknown";
                return (true, status, null);
            }

            return (false, "unknown", $"HTTP {response.StatusCode}: {responseContent}");
        }
        catch (Exception ex)
        {
            return (false, "unknown", ex.Message);
        }
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}
