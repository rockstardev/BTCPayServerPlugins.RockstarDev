using System.Collections.Generic;
using Strike.Client.PaymentMethods;
using Strike.Client.ReceiveRequests;

namespace BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.ViewModels.RockstarStrikeUtils;

public class PaymentMethodsViewModel
{
    public List<PaymentMethod> List { get; set; }

    public int TotalCount { get; set; }
}