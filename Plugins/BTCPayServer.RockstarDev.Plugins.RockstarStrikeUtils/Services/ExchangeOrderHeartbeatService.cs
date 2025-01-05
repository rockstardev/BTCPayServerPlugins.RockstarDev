using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Data;
using BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Data.Models;
using BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.ViewModels.ExchangeOrder;

namespace BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Services;

public class ExchangeOrderHeartbeatService(
    EventAggregator eventAggregator,
    Logs logs,
    RockstarStrikeDbContextFactory strikeDbContextFactory) : EventHostedServiceBase(eventAggregator, logs), IPeriodicTask
{
    private Dictionary<string, DateTimeOffset> _lastRunForStore = new();
    protected override void SubscribeToEvents()
    {
        base.SubscribeToEvents();
    }

    public Task Do(CancellationToken cancellationToken)
    {
        var db = strikeDbContextFactory.CreateContext();
        var stores = db.Settings.Where(a=>a.Key == DbSettingKeys.ExchangeOrderSettings.ToString()).ToList();
        foreach (var store in stores)
        {
            if (!_lastRunForStore.TryGetValue(store.StoreId, out var lastRun))
            {
                lastRun = DateTimeOffset.MinValue;
            }

            var setting = SettingsViewModel.FromDbSettings(store);
            if (lastRun.AddMinutes(setting.MinutesHeartbeatInterval) < DateTimeOffset.UtcNow)
            {
                lastRun = DateTimeOffset.UtcNow;
                _lastRunForStore[store.StoreId] = lastRun;
                PushEvent(new PeriodProcessEvent());
            }
        }
        
        return Task.CompletedTask;
    }

    private class PeriodProcessEvent { } 
    
    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        if (evt is PeriodProcessEvent)
        {
            // Do something
        }
    }
}