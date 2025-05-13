using System;
using BTCPayServer.RockstarDev.Plugins.PluginCounter.ViewModels;

namespace BTCPayServer.RockstarDev.Plugins.PluginCounter;

public class CounterPluginSettings
{
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool Enabled { get; set; }
    public bool AllStores { get; set; }
    public string? Password { get; set; }
    public string AdminUserId { get; set; }
    public SelectedStoreViewModel[] SelectedStores { get; set; }
}
