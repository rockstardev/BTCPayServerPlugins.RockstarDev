using System;
using System.Collections.Generic;
using Strike.Client.Deposits;
using Strike.Client.Models;

namespace BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.ViewModels.RockstarStrikeUtils;

public class DepositsViewModel
{
    public List<Deposit> Deposits { get; set; }

    public int TotalCount { get; set; }
}

public class DepositsCreateViewModel
{
    public Guid StrikePaymentMethodId { get; set; }
    public string Amount { get; set; }
    public FeePolicy FeePolicy { get; set; }
}
