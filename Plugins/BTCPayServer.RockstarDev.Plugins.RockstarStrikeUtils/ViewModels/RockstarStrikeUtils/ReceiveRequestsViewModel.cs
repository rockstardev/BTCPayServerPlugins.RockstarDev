using System.Collections.Generic;
using Strike.Client.ReceiveRequests;

namespace BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.ViewModels.RockstarStrikeUtils;

public class ReceiveRequestsViewModel
{
    public List<ReceiveRequest> ReceiveRequests { get; set; }

    public int TotalCount { get; set; }
}
}