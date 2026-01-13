using System;

namespace BTCPayServer.RockstarDev.Plugins.WalletSweeper.ViewModels;

public class FeeRateOption
{
    public TimeSpan Target { get; set; }
    public decimal FeeRate { get; set; }
}
