using System.Collections.Generic;
using BTCPayServer.Plugins.PointOfSale.Models;

namespace BTCPayServer.RockstarDev.Plugins.Vouchers.ViewModel;

public class VoucherPointOfSaleViewModel : ViewPointOfSaleViewModel
{
    public List<StorePosAppItem> PoSApps { get; set; }
    public string CurrentAppId { get; set; }

}

public class StorePosAppItem
{
    public string Name { get; set; }
    public string Url { get; set; }
}
