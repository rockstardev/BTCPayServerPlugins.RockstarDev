using System.Collections.Generic;

namespace BTCPayServer.RockstarDev.Plugins.Vouchers.ViewModel;

public class VoucherSettings
{
    public bool FunModeEnabled { get; set; }
    public string SelectedVoucherImage { get; set; }
}


public class VoucherSettingsViewModel
{
    public string StoreId { get; set; }
    public bool FunModeEnabled { get; set; }
    public string SelectedVoucherImage { get; set; }
    public List<string> VoucherOptions { get; set; } = new() { "jack", "odell", "giacomo", "luke" };
}
