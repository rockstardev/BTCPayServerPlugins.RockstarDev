using System.Collections.Generic;
using BTCPayServer.Models;
using BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Data.Models;

namespace BTCPayServer.RockstarDev.Plugins.BitcoinStacker.ViewModels.ExchangeOrder;

public class IndexViewModel : BasePagingViewModel
{
    public List<DbExchangeOrder> List { get; set; } = new();
    public string BTCBalance { get; set; }
    public string USDBalance { get; set; }

    public string StackerTotalUsdCost { get; set; }
    public string StackerTotalBitcoin { get; set; }
    public string StackerProfitUSD { get; set; }

    public override int CurrentPageCount => List?.Count ?? 0;
}
