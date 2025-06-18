using BTCPayServer.Plugins.POSTester.Configuration;
using BTCPayServer.Plugins.POSTester.Services;
using Microsoft.Extensions.Configuration;

namespace BTCPayServer.Plugins.POSTester;

class Program
{
    static async Task<int> Main(string[] args)
    {
        Console.WriteLine("BTCPay Server POS Performance Tester");
        Console.WriteLine("====================================");

        try
        {
            // Build configuration
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true)
                .AddEnvironmentVariables("POSTESTER_")
                .AddCommandLine(args)
                .Build();

            var config = new TestConfiguration();
            configuration.Bind(config);

            // Validate configuration
            if (!ValidateConfiguration(config))
            {
                PrintUsage();
                return 1;
            }

            // Install Playwright if needed
            Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] Ensuring Playwright browsers are installed...");
            var exitCode = Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });
            if (exitCode != 0)
            {
                Console.WriteLine("Warning: Playwright browser installation may have failed");
            }

            // Run the test
            using var tester = new CheckoutTester(config);
            var result = await tester.RunTestAsync();

            return result.Success ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] Fatal error: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    private static bool ValidateConfiguration(TestConfiguration config)
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(config.CheckoutUrl))
            errors.Add("CheckoutUrl is required");

        if (config.ExtractOnly)
        {
            Console.WriteLine("Running in ExtractOnly mode - API credentials not required");
        }
        else
        {
            if (string.IsNullOrEmpty(config.BTCPayServerUrl))
                errors.Add("BTCPayServerUrl is required");

            if (string.IsNullOrEmpty(config.ApiKey))
                errors.Add("ApiKey is required");

            if (string.IsNullOrEmpty(config.StoreId))
                errors.Add("StoreId is required");
        }

        if (config.Amount <= 0)
            errors.Add("Amount must be greater than 0");

        if (!Uri.TryCreate(config.CheckoutUrl, UriKind.Absolute, out _))
            errors.Add("CheckoutUrl must be a valid URL");

        if (!config.ExtractOnly && !string.IsNullOrEmpty(config.BTCPayServerUrl) && 
            !Uri.TryCreate(config.BTCPayServerUrl, UriKind.Absolute, out _))
            errors.Add("BTCPayServerUrl must be a valid URL");

        if (errors.Any())
        {
            Console.WriteLine("Configuration errors:");
            foreach (var error in errors)
            {
                Console.WriteLine($"- {error}");
            }
            return false;
        }

        return true;
    }

    private static void PrintUsage()
    {
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run -- --CheckoutUrl https://example.btcpay.tech/apps/xyz/pos \\");
        Console.WriteLine("                --BTCPayServerUrl https://example.btcpay.tech \\");
        Console.WriteLine("                --ApiKey your_api_key \\");
        Console.WriteLine("                --StoreId your_store_id \\");
        Console.WriteLine("                --Amount 0.10");
        Console.WriteLine();
        Console.WriteLine("Or set environment variables:");
        Console.WriteLine("  POSTESTER_CheckoutUrl=https://example.btcpay.tech/apps/xyz/pos");
        Console.WriteLine("  POSTESTER_BTCPayServerUrl=https://example.btcpay.tech");
        Console.WriteLine("  POSTESTER_ApiKey=your_api_key");
        Console.WriteLine("  POSTESTER_StoreId=your_store_id");
        Console.WriteLine("  POSTESTER_Amount=0.10");
        Console.WriteLine();
        Console.WriteLine("Or create appsettings.json:");
        Console.WriteLine("{");
        Console.WriteLine("  \"CheckoutUrl\": \"https://example.btcpay.tech/apps/xyz/pos\",");
        Console.WriteLine("  \"BTCPayServerUrl\": \"https://example.btcpay.tech\",");
        Console.WriteLine("  \"ApiKey\": \"your_api_key\",");
        Console.WriteLine("  \"StoreId\": \"your_store_id\",");
        Console.WriteLine("  \"Amount\": 0.10,");
        Console.WriteLine("  \"TimeoutSeconds\": 60,");
        Console.WriteLine("  \"Headless\": true,");
        Console.WriteLine("  \"SlowMo\": 100");
        Console.WriteLine("}");
    }
}
