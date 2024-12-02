using System.Collections.Generic;
using Strike.Client.ReceiveRequests;

namespace BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.ViewModels.RockstarStrikeUtils;

public class ReceiveRequestsViewModel
{
    public List<ReceiveRequest> ReceiveRequests { get; set; }

    public int TotalCount { get; set; }
}

public class ReceiveRequestsCreateViewModel
{
    public string TargetCurrency { get; set; }

    public bool Bolt11 { get; set; }

    public bool Bolt12 { get; set; }

    public bool Onchain { get; set; }
}