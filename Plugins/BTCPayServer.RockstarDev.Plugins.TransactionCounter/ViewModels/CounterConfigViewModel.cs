using System;
using System.ComponentModel.DataAnnotations;
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
    public string? Password { get; set; }
    public StoreData[] Stores { get; set; }

    [Display(Name = "HTML Template")]
    public string? HtmlTemplate { get; set; }

    [Display(Name = "Custom Transactions")]
    public string ExtraTransactions { get; set; }
    public string ExcludedStoreIds { get; set; }

    public record Defaults
    {
        public const string HtmlTemplate = @"<!DOCTYPE html>
<html>
<head>
  <meta charset=""UTF-8"">
  <title>Bitcoin Transactions</title>
  <style>
    html, body {
      margin: 0;
      padding: 0;
      overflow: hidden;
      height: 100%;
      font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
    }
    video.bg-video {
      position: fixed;
      right: 0;
      bottom: 0;
      min-width: 100%;
      min-height: 100%;
      z-index: -1;
      object-fit: cover;
    }
    .counter-box {
      position: absolute;
      top: 50%;
      left: 50%;
      transform: translate(-50%, -50%);
      text-align: center;
      font-size: 4em;
      font-weight: bold;
      color: white;
      text-shadow: 0 0 10px #000;
      animation: fadeIn 2s ease-out;
    }
    @keyframes fadeIn {
      from { opacity: 0; transform: translate(-50%, -40%); }
      to { opacity: 1; transform: translate(-50%, -50%); }
    }
  </style>
</head>
<body>
  <!-- Replace with your own .mp4 URL -->
  <video class=""bg-video"" autoplay muted loop playsinline>
    <source src=""https://v.nostr.build/MlvwiKZlMbCmrjsU.mp4"" type=""video/mp4"">
    Your browser does not support the video tag.
  </video>
  <div class=""counter-box"">
    <span id=""tx-count"">{COUNTER}</span>
  </div>
</body>
</html>";
    }
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
}
