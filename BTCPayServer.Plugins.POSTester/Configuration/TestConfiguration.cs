namespace BTCPayServer.Plugins.POSTester.Configuration;

public class TestConfiguration
{
    public string CheckoutUrl { get; set; } = string.Empty;
    public decimal Amount { get; set; } = 0.10m;
    public string BTCPayServerUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string StoreId { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 60;
    public bool Headless { get; set; } = true;
    public int SlowMo { get; set; } = 100;
    public bool ExtractOnly { get; set; } = false;
}
