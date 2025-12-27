namespace BTCPayServer.RockstarDev.Plugins.Vouchers.ViewModel;

public class VoucherSettings
{
    public bool FunModeEnabled { get; set; }
}


public class VoucherSettingsViewModel
{
    public string StoreId { get; set; }
    public bool FunModeEnabled { get; set; }
    public string SearchText { get; set; }
}
