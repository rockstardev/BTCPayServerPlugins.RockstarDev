using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Data;
using BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Data.Models;
using BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Logic;
using BTCPayServer.RockstarDev.Plugins.BitcoinStacker.ViewModels.ExchangeOrder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.Extensions.Logging;
using Strike.Client;
using Strike.Client.CurrencyExchanges;
using Strike.Client.Deposits;
using Strike.Client.Models;

namespace BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Services;

public class ExchangeOrderHeartbeatService(
    EventAggregator eventAggregator,
    Logs logs,
    PluginDbContextFactory strikeDbContextFactory,
    StrikeClientFactory strikeClientFactory,
    StripeClientFactory stripeClientFactory) : EventHostedServiceBase(eventAggregator, logs), IPeriodicTask
{
    private Dictionary<string, DateTimeOffset> _lastRunForStore = new();
    protected override void SubscribeToEvents()
    {
        Subscribe<PeriodProcessEvent>();
        base.SubscribeToEvents();
    }

    private bool _isItFirstRun = true;
    public Task Do(CancellationToken cancellationToken)
    {
        if (_isItFirstRun)
        {
            _isItFirstRun = false;
            return Task.CompletedTask; // do not run the service when server is starting
        }

        using var db = strikeDbContextFactory.CreateContext();
        var stores = db.Settings.Where(a=>a.Key == DbSettingKeys.ExchangeOrderSettings.ToString()).ToList();
        foreach (var store in stores)
        {
            if (!_lastRunForStore.TryGetValue(store.StoreId, out var lastRun))
            {
                lastRun = DateTimeOffset.MinValue;
            }

            var setting = SettingsViewModel.FromDbSettings(store);
            if (!setting.AutoEnabled)
                continue;
            
            if (lastRun.AddMinutes(setting.MinutesHeartbeatInterval) < DateTimeOffset.UtcNow)
            {
                _lastRunForStore[store.StoreId] = DateTimeOffset.UtcNow;
                PushEvent(new PeriodProcessEvent { StoreId = store.StoreId, Setting = setting});
            }
        }
        
        return Task.CompletedTask;
    }

    public class PeriodProcessEvent
    {
        public string StoreId { get; set; }
        public SettingsViewModel Setting { get; set; }
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        return base.StartAsync(cancellationToken);
    }


    public static readonly DateTimeOffset DELAY_UNTIL = new(2026, 01, 01, 0, 0, 0, DateTimeOffset.UtcNow.Offset);

    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        if (evt is PeriodProcessEvent ppe)
        {
            _lastRunForStore[ppe.StoreId] = DateTimeOffset.UtcNow;
            
            Logs.PayServer.LogInformation("ExchangeOrderHeartbeatService: Executing");
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
                if (payout.Status != "paid")
                    continue; // only process paid payouts

                // Stripe uses cents
                var amt = Math.Round(payout.Amount / 100.0m * (settings.PercentageOfPayouts / 100), 2);
                var exchangeOrder = new DbExchangeOrder
                {
                    StoreId = ppe.StoreId,
                    Operation = DbExchangeOrder.Operations.BuyBitcoin,
                    Amount = amt, 
                    Created = DateTimeOffset.UtcNow,
                    CreatedBy = DbExchangeOrder.CreateByTypes.Automatic.ToString(),
                    CreatedForDate = payout.Created,
                    State = DbExchangeOrder.States.Created,
                    DelayUntil = DELAY_UNTIL
                };
                db.ExchangeOrders.Add(exchangeOrder);
            }
            await db.SaveChangesAsync(cancellationToken);

            if (String.IsNullOrEmpty(settings.StrikeApiKey))
                return;
            
            // get the list of orders in created mode and initiate deposits
            var orders = db.ExchangeOrders
                .Where(a => a.StoreId == ppe.StoreId && a.Operation == DbExchangeOrder.Operations.BuyBitcoin
                             && a.State == DbExchangeOrder.States.Created
                             && (a.DelayUntil == null || a.DelayUntil < DateTimeOffset.UtcNow))
                .OrderBy(a => a.CreatedForDate)
                .ThenBy(a=> a.Created)
                .ToList();
            Logs.PayServer.LogInformation("ExchangeOrderHeartbeatService: Initiating deposits on Strike for {0} orders", orders.Count);
            
            var strikeClient = strikeClientFactory.InitClient(settings.StrikeApiKey);

            foreach (var order in orders)
            {
                var balanceResp = await strikeClient.Balances.GetBalances();
                var usdBalance = balanceResp.FirstOrDefault(a => a.Currency == Currency.Usd)?.Available ?? 0;
                
                var req = new DepositReq
                {
                    PaymentMethodId = settings.StrikePaymentMethodId,
                    Amount = order.Amount.ToString(CultureInfo.InvariantCulture),
                    Fee = FeePolicy.Exclusive
                };
                db.AddExchangeOrderLogs(order.Id, DbExchangeOrderLog.Events.CreatingDeposit, req);
                await db.SaveChangesAsync(cancellationToken);
                
                var resp = await strikeClient.Deposits.Create(req);
                if (resp.IsSuccessStatusCode)
                {
                    order.State = DbExchangeOrder.States.DepositWaiting;
                    db.AddExchangeOrderLogs(order.Id, DbExchangeOrderLog.Events.DepositCreated, resp, resp.Id.ToString());
                }
                else
                {
                    order.State = DbExchangeOrder.States.Error;
                    db.AddExchangeOrderLogs(order.Id, DbExchangeOrderLog.Events.Error, resp);
                }
                await db.SaveChangesAsync(cancellationToken);
                
                // check balance and if it increased execute purchase immediately
                await Task.Delay(5000, cancellationToken).WaitAsync(cancellationToken);

                balanceResp = await strikeClient.Balances.GetBalances();
                var usdBalanceAfter = balanceResp.FirstOrDefault(a => a.Currency == Currency.Usd)?.Available ?? 0;
                
                if (usdBalanceAfter - usdBalance >= order.Amount)
                {
                    Logs.PayServer.LogInformation("ExchangeOrderHeartbeatService: Exchange Order {0} deposit completed on Strike, executing", order.Id);
                    await ExecuteConversionOrder(cancellationToken, order, db, strikeClient);
                }
            }
            
            // 
            var waitingOrders = db.ExchangeOrders
                .Include(order => 
                    order.ExchangeOrderLogs.Where(log => log.Event == DbExchangeOrderLog.Events.DepositCreated))
                .Where(order => order.StoreId == ppe.StoreId 
                                && order.State == DbExchangeOrder.States.DepositWaiting
                                && (order.DelayUntil == null || order.DelayUntil < DateTimeOffset.UtcNow))
                .OrderBy(a => a.CreatedForDate)
                .ThenBy(a=> a.Created)
                .ToList();
            Logs.PayServer.LogInformation("ExchangeOrderHeartbeatService: {0} orders waiting on deposit on Strike", waitingOrders.Count);
            
            foreach (var order in waitingOrders)
            {
                var depositingId = Guid.Parse(order.ExchangeOrderLogs.First().Parameter);
                var resp = await strikeClient.Deposits.FindDeposit(depositingId);

                if (!resp.IsSuccessStatusCode)
                {
                    order.State = DbExchangeOrder.States.Error;
                    db.AddExchangeOrderLogs(order.Id, DbExchangeOrderLog.Events.Error, resp);
                    
                    // exiting the loop
                    continue;
                }
                    
                if (resp.State == DepositState.Completed)
                {
                    Logs.PayServer.LogInformation("ExchangeOrderHeartbeatService: Exchange Order {0} deposit completed on Strike, executing", order.Id);
                    await ExecuteConversionOrder(cancellationToken, order, db, strikeClient);
                }
                else if (resp.State == DepositState.Pending)
                {
                    // Do nothing
                }
                else
                {
                    // Failed and Reversed deposits will also end up here
                    
                    // TODO: If deposit failed, initiate it again
                    
                    order.State = DbExchangeOrder.States.Error;
                    db.AddExchangeOrderLogs(order.Id, DbExchangeOrderLog.Events.Error, resp);
                }
            }
        }
    }

    public static async Task ExecuteConversionOrder(CancellationToken cancellationToken, DbExchangeOrder order,
        PluginDbContext db, StrikeClient strikeClient)
    {
        var req = new CurrencyExchangeQuoteReq
        {
            Buy = Currency.Btc, Sell = Currency.Usd,
            Amount = new MoneyWithFee
            {
                Currency = Currency.Usd, Amount = order.Amount, FeePolicy = FeePolicy.Inclusive
            }
        };
        db.AddExchangeOrderLogs(order.Id, DbExchangeOrderLog.Events.ExecutingExchange, req);
        await db.SaveChangesAsync(cancellationToken);
                    
        //
        var exchangeResp = await strikeClient.CurrencyExchanges.CreateQuote(req);
        if (!exchangeResp.IsSuccessStatusCode)
        {
            order.State = DbExchangeOrder.States.Error;
            db.AddExchangeOrderLogs(order.Id, DbExchangeOrderLog.Events.Error, exchangeResp);
                        
            // exiting the loop
            return;
        }
                    
        var executeQuoteResp = await strikeClient.CurrencyExchanges.ExecuteQuote(exchangeResp.Id);
        if (!executeQuoteResp.IsSuccessStatusCode)
        {
            order.State = DbExchangeOrder.States.Error;
            db.AddExchangeOrderLogs(order.Id, DbExchangeOrderLog.Events.Error, executeQuoteResp);
                        
            // exiting the loop
            return;
        }
        
        order.State = DbExchangeOrder.States.Completed;
        db.AddExchangeOrderLogs(order.Id, DbExchangeOrderLog.Events.ExchangeExecuted, executeQuoteResp);
        await db.SaveChangesAsync(cancellationToken);
    }
}