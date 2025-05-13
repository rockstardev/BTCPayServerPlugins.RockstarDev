using BTCPayServer.Models;

namespace BTCPayServer.RockstarDev.Plugins.TransactionCounter.ViewModels;

public class BaseCounterPublicViewModel
{
    public string StoreId { get; set; }
    public string StoreName { get; set; }
    public StoreBrandingViewModel StoreBranding { get; set; }
}
