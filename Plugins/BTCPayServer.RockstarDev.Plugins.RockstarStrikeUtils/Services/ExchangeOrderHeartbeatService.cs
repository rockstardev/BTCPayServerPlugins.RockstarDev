using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Data;
using BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Data.Models;
using BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Logic;
using BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.ViewModels.ExchangeOrder;

namespace BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Services;

public class ExchangeOrderHeartbeatService(
    EventAggregator eventAggregator,
    Logs logs,
    RockstarStrikeDbContextFactory strikeDbContextFactory,
    StrikeClientFactory strikeClientFactory,
    StripeClientFactory stripeClientFactory) : EventHostedServiceBase(eventAggregator, logs), IPeriodicTask
{
    private Dictionary<string, DateTimeOffset> _lastRunForStore = new();
    protected override void SubscribeToEvents()
    {
        base.SubscribeToEvents();
    }

    public Task Do(CancellationToken cancellationToken)
    {
        using var db = strikeDbContextFactory.CreateContext();
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
                PushEvent(new PeriodProcessEvent { StoreId = store.StoreId, Setting = setting});
            }
        }
        
        return Task.CompletedTask;
    }

    private class PeriodProcessEvent
    {
        public string StoreId { get; set; }
        public SettingsViewModel Setting { get; set; }
    } 
    
    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        if (evt is PeriodProcessEvent ppe)
        {
            await using var db = strikeDbContextFactory.CreateContext();

            var lastOrder = db.ExchangeOrders
                .Where(a => a.StoreId == ppe.StoreId && a.Operation == DbExchangeOrder.Operations.BuyBitcoin
                                                     && a.CreatedBy ==
                                                     DbExchangeOrder.CreateByTypes.Automatic.ToString())
                .OrderByDescending(a => a.CreatedForDate)
                .FirstOrDefault();

            var settings = ppe.Setting;

            DateTimeOffset dateToFetch = lastOrder?.CreatedForDate ?? (settings.StartDateExchangeOrders ?? DateTimeOffset.UtcNow);

            // create list of orders to execute from payouts
            var payouts = await stripeClientFactory.PayoutsSince(settings.StripeApiKey, dateToFetch);
            payouts = payouts.OrderBy(a=>a.Created).ToList();
            foreach (var payout in payouts)
            {
                var exchangeOrder = new DbExchangeOrder
                {
                    StoreId = ppe.StoreId,
                    Operation = DbExchangeOrder.Operations.BuyBitcoin,
                    Amount = payout.Amount / 100.0m * settings.PercentageOfPayouts, // Stripe uses cents
                    Created = DateTimeOffset.UtcNow,
                    CreatedBy = DbExchangeOrder.CreateByTypes.Automatic.ToString(),
                    CreatedForDate = payout.Created,
                    State = DbExchangeOrder.States.Created,
                    DelayUntil = new DateTimeOffset(2026, 01, 01, 0, 0, 0, DateTimeOffset.UtcNow.Offset)
                };
                db.ExchangeOrders.Add(exchangeOrder);
            }
            await db.SaveChangesAsync(cancellationToken);
            
            // get the list of orders in created mode and initiate deposits
            var orders = db.ExchangeOrders
                .Where(a => a.StoreId == ppe.StoreId && a.Operation == DbExchangeOrder.Operations.BuyBitcoin
                             && a.State == DbExchangeOrder.States.Created
                             && a.DelayUntil < DateTimeOffset.UtcNow)
                .OrderBy(a => a.Created)
                .ToList();
            var strikeClient = strikeClientFactory.InitClient(settings.StrikeApiKey);
            foreach (var order in orders)
            {
                // TODO: push deposits and record them on the exchange order
            }
        }
    }
}