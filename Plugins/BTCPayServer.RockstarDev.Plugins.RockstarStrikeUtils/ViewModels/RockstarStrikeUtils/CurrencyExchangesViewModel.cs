using System.Collections.Generic;
using Strike.Client.Balances;
using Strike.Client.CurrencyExchanges;
using Strike.Client.ReceiveRequests;

namespace BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.ViewModels.RockstarStrikeUtils;

public class CurrencyExchangesViewModel
{
    public List<Balance> Balances { get; set; }

    public decimal UsdAmount { get; set; }
}

public class CurrencyExchangesCreateViewModel
{
    public CurrencyExchangeQuote Quote { get; set; }

    public string Sell { get; set; }
    public decimal SellAmount { get; set; }
    public string Buy { get; set; }
    public decimal BuyAmount { get; set; }
    public decimal ExchangeRate { get; set; }
}

