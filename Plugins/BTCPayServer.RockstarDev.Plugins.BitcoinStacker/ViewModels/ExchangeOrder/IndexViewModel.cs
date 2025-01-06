using System.Collections.Generic;
using BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Data.Models;

namespace BTCPayServer.RockstarDev.Plugins.BitcoinStacker.ViewModels.ExchangeOrder;

public class IndexViewModel
{
    public List<DbExchangeOrder> List { get; set; }
}