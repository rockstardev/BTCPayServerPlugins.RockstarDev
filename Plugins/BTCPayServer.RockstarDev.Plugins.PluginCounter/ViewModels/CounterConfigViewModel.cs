using System;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Data;

namespace BTCPayServer.RockstarDev.Plugins.PluginCounter.ViewModels;

public class CounterConfigViewModel
{
    [Display(Name = "Start Date")]
    public DateTime StartDate { get; set; }
    [Display(Name = "End Date")]
    public DateTime? EndDate { get; set; }
    [Display(Name = "Enable Counter configuration")]
    public bool Enabled { get; set; }
    [Display(Name = "Enable counter for all stores")]
    public bool AllStores { get; set; }
    public string? Password { get; set; }

    [Display(Name = "Selected Stores")]
    public SelectedStoreViewModel[] SelectedStores { get; set; }
    public StoreData[] Stores { get; set; }
}

public class SelectedStoreViewModel
{
    public string Id { get; set; }
    public string Name { get; set; }
    public bool Enabled { get; set; }
}


public class CounterViewModel
{
    public int TransactionCount { get; set; }
    public string? VideoUrl { get; set; }
}
