using System.Collections.Generic;
using Strike.Client.PaymentMethods;

namespace BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.ViewModels.RockstarStrikeUtils;

public class PaymentMethodsViewModel
{
    public List<PaymentMethod> List { get; set; }

    public int TotalCount { get; set; }
}
