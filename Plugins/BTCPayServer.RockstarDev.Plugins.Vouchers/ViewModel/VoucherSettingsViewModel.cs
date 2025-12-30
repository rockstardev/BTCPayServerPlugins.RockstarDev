using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace BTCPayServer.RockstarDev.Plugins.Vouchers.ViewModel;

public class VoucherSettings
{
    public bool FunModeEnabled { get; set; }
    public string SelectedVoucherImage { get; set; }
    public bool SpreadEnabled { get; set; }
    public decimal SpreadPercentage { get; set; }
    public List<VoucherImageSettings> Images { get; set; } = new();
}

public class VoucherImageSettings
{
    public string Key { get; set; }
    public string Name { get; set; } 
    public string StoredFileId { get; set; }
    public string FileUrl { get; set; }
    public bool Enabled { get; set; } = true;
}

public class VoucherSettingsViewModel
{
    public string StoreId { get; set; }
    public bool FunModeEnabled { get; set; }
    public bool SpreadEnabled { get; set; }
    public decimal SpreadPercentage { get; set; }
    public string SelectedVoucherImage { get; set; }
    public List<string> VoucherOptions { get; set; } = new() { "jack", "odell", "giacomo", "luke", "nayib" };
}


public class VoucherImageSettingsViewModel
{
    public string NewImageTitle { get; set; }
    public IFormFile NewImageFile { get; set; }
    public List<VoucherImageSettings> Images { get; set; } = new();
}
