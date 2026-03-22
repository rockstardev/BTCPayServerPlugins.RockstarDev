using System.Collections.Generic;

namespace BTCPayServer.RockstarDev.Plugins.VendorPay.ViewModels;

public class VendorPayLabelViewModel
{
    public string Label { get; set; }
    public string Color { get; set; }
    public string TextColor { get; set; }
}

public class VendorPayLabelsViewModel
{
    public string StoreId { get; set; }
    public List<VendorPayLabelViewModel> Labels { get; set; } = new();
}
