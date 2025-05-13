using System;
using BTCPayServer.Data;
using BTCPayServer.RockstarDev.Plugins.PluginCounter.ViewModels;

namespace BTCPayServer.RockstarDev.Plugins.PluginCounter;

public class CounterPluginSettings
{
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool Enabled { get; set; }
    public bool AllStores { get; set; }
    public SelectedStoreViewModel[] SelectedStores { get; set; }
}
