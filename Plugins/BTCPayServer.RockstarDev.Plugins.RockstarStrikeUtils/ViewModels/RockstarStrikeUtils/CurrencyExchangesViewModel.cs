using System.Collections.Generic;
using Strike.Client.Balances;
using Strike.Client.ReceiveRequests;

namespace BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.ViewModels.RockstarStrikeUtils;

public class CurrencyExchangesViewModel
{
    public List<Balance> Balances { get; set; }
}