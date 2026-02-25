namespace BTCPayServer.RockstarDev.Plugins.WithdrawalProvider;

public class WithdrawalProviderSettings
{
    public const string SettingsName = "RockstarDev.WithdrawalProvider";

    public bool Enabled { get; set; }
    public string ApiKey { get; set; } = string.Empty;
    public string Ticker { get; set; } = "BTCEUR";
    public string FiatCurrency { get; set; } = "EUR";
    public string PaymentMethod { get; set; } = "LIGHTNING";
}
