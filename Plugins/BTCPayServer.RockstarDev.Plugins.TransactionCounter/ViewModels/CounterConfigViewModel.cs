using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Reflection;
using BTCPayServer.Data;

namespace BTCPayServer.RockstarDev.Plugins.TransactionCounter.ViewModels;

public class CounterConfigViewModel
{
    [Display(Name = "Start Date")]
    public DateTime StartDate { get; set; }

    [Display(Name = "End Date")]
    public DateTime? EndDate { get; set; }

    [Display(Name = "Enable transaction counter configuration")]
    public bool Enabled { get; set; }

    [Display(Name = "Include archived invoices")]
    public bool IncludeArchived { get; set; }

    [Display(Name = "Include Transaction Volume Data (more expensive queries)")]
    public bool IncludeTransactionVolume { get; set; }

    public string? Password { get; set; }
    public StoreData[] Stores { get; set; }

    [Display(Name = "HTML Template")]
    public string? HtmlTemplate { get; set; }

    [Display(Name = "Custom Transactions")]
    public string ExtraTransactions { get; set; }

    public string ExcludedStoreIds { get; set; }
}

public class ExtraTransactionEntry
{
    public string Source { get; set; }
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public int Count { get; set; }
}

public class CounterViewModel : BaseCounterPublicViewModel
{
    public string HtmlTemplate { get; set; }
    public int InitialCount { get; set; }
    public Dictionary<string, decimal> InitialVolumeByCurrency { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
