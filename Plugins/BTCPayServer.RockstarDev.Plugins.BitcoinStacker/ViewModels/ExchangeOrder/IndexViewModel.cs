using System.Collections.Generic;
using BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Data.Models;

namespace BTCPayServer.RockstarDev.Plugins.BitcoinStacker.ViewModels.ExchangeOrder;

public class IndexViewModel
{
    public List<DbExchangeOrder> List { get; set; }
    public string BTCBalance { get; set; }
    public string USDBalance { get; set; }
}
