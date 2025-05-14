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
    public static readonly DateTimeOffset DELAY_UNTIL = new(2026, 01, 01, 0, 0, 0, DateTimeOffset.UtcNow.Offset);
    private readonly Dictionary<string, DateTimeOffset> _lastRunForStore = new();

    private bool _isItFirstRun = true;

    public Task Do(CancellationToken cancellationToken)
    {
        // NEW LOG: Indicate Do method has started
        Logs.PayServer.LogInformation($"{GetType().Name}: Do method invoked.");

        if (cancellationToken.IsCancellationRequested)
        {
            // NEW LOG: Cancellation requested at the start of Do
            Logs.PayServer.LogInformation($"{GetType().Name}: Cancellation requested at the very start of Do method. Exiting.");
            return Task.CompletedTask;
        }

        if (_isItFirstRun)
        {
            _isItFirstRun = false;
            // NEW LOG: First run skipped
            Logs.PayServer.LogInformation($"{GetType().Name}: First run after service start, skipping actual work in Do method as intended.");
            return Task.CompletedTask;
        }

        List<DbSetting> stores;
        try
        {
            using var db = strikeDbContextFactory.CreateContext();
            stores = db.Settings.Where(a => a.Key == nameof(DbSettingKeys.ExchangeOrderSettings)).ToList();
            // NEW LOG: Number of stores fetched
            Logs.PayServer.LogInformation($"{GetType().Name}: Fetched {stores.Count} store settings objects.");
        }
        catch (Exception ex)
        {
            Logs.PayServer.LogError(ex, $"{GetType().Name}: Error fetching stores in Do method. Service will not process any stores in this cycle.");
            return Task.CompletedTask; 
        }

        if (!stores.Any())
        {
            Logs.PayServer.LogInformation($"{GetType().Name}: No stores found with ExchangeOrderSettings. Do method exiting.");
            return Task.CompletedTask;
        }

        int eventsPushedThisCycle = 0; // NEW COUNTER
        foreach (var store in stores)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                Logs.PayServer.LogInformation($"{GetType().Name}: Cancellation requested mid-loop in Do method while processing store {store?.StoreId}. Exiting loop.");
                break;
            }

            try
            {
                // NEW LOG: Processing a specific store
                Logs.PayServer.LogInformation($"{GetType().Name}: Processing store {store.StoreId} in Do method.");

                if (!_lastRunForStore.TryGetValue(store.StoreId, out var lastRun))
                {
                    lastRun = DateTimeOffset.MinValue;
                    Logs.PayServer.LogInformation($"{GetType().Name}: Store {store.StoreId} not in _lastRunForStore. Initialized lastRun to MinValue.");
                }

                var setting = SettingsViewModel.FromDbSettings(store);
                if (!setting.AutoEnabled)
                {
                    // NEW LOG: AutoExecute not enabled
                    Logs.PayServer.LogInformation($"{GetType().Name}: AutoExecute not enabled for store {store.StoreId}. Skipping.");
                    continue;
                }

                var nextRunTime = lastRun.AddMinutes(setting.MinutesHeartbeatInterval);
                var currentTime = DateTimeOffset.UtcNow;

                // NEW LOG: Detailed timing information
                Logs.PayServer.LogInformation($"{GetType().Name}: Store {store.StoreId} - LastRun: {lastRun:o}, Interval: {setting.MinutesHeartbeatInterval} mins, Calculated NextRunTime: {nextRunTime:o}, CurrentTime: {currentTime:o}");

                if (nextRunTime < currentTime)
                {
                    Logs.PayServer.LogInformation($"{GetType().Name}: Time condition met for store {store.StoreId}. Pushing PeriodProcessEvent. (NextRunTime {nextRunTime:o} < CurrentTime {currentTime:o})");
                    _lastRunForStore[store.StoreId] = currentTime; // Update with current time when pushing
                    PushEvent(new PeriodProcessEvent { StoreId = store.StoreId, Setting = setting });
                    eventsPushedThisCycle++; // NEW: Increment counter
                }
                else
                {
                    // NEW LOG: Time condition not met
                    Logs.PayServer.LogInformation($"{GetType().Name}: Time condition NOT met for store {store.StoreId}. Next run in approx. {(nextRunTime - currentTime).TotalMinutes:F2} minutes.");
                }
            }
            catch (Exception ex)
            {
                Logs.PayServer.LogError(ex, $"{GetType().Name}: Error processing store {store?.StoreId} in Do method's inner try-catch. Skipping this store for this cycle.");
            }
        }

        // NEW LOG: Do method finished
        Logs.PayServer.LogInformation($"{GetType().Name}: Do method finished. Pushed {eventsPushedThisCycle} events this cycle. CancellationRequested: {cancellationToken.IsCancellationRequested}");
        return Task.CompletedTask;
    }

    protected override void SubscribeToEvents()
    {
        Subscribe<PeriodProcessEvent>();
        base.SubscribeToEvents();
    }

    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        if (evt is PeriodProcessEvent ppe) await PeriodProcessEventWork(ppe, cancellationToken);
    }

    private async Task PeriodProcessEventWork(PeriodProcessEvent ppe, CancellationToken cancellationToken)
    {
        try
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

            var dateToFetch =
                lastOrder?.CreatedForDate ?? (settings.StartDateExchangeOrders ?? DateTimeOffset.UtcNow);

            // create list of orders to execute from payouts
            if (!string.IsNullOrEmpty(settings.StripeApiKey))
            {
                var payouts = await stripeClientFactory.PayoutsSince(settings.StripeApiKey, dateToFetch);
                payouts = payouts.OrderBy(a => a.Created).ToList();
                foreach (var payout in payouts)
                {
                    if (payout.Status != "paid" || payout.Amount <= 10000)
                        continue; // only process paid payouts larger than $100.00

                    DateTimeOffset? delayUntil = null;
                    var delay = settings.DelayOrderDays ?? 365;
                    if (delay > 0)
                        delayUntil = DateTimeOffset.UtcNow.AddDays(delay);

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
                        DelayUntil = delayUntil
                    };
                    db.ExchangeOrders.Add(exchangeOrder);
                }

                await db.SaveChangesAsync(cancellationToken);
            }

            if (string.IsNullOrEmpty(settings.StrikeApiKey))
                return;

            // get the list of orders in created mode and initiate deposits
            var orders = db.ExchangeOrders
                .Where(a => a.StoreId == ppe.StoreId && a.Operation == DbExchangeOrder.Operations.BuyBitcoin
                                                     && a.State == DbExchangeOrder.States.Created
                                                     && (a.DelayUntil == null || a.DelayUntil < DateTimeOffset.UtcNow))
                .OrderBy(a => a.CreatedForDate)
                .ThenBy(a => a.Created)
                .ToList();
            Logs.PayServer.LogInformation("ExchangeOrderHeartbeatService: Initiating deposits on Strike for {0} orders",
                orders.Count);

            var strikeClient = strikeClientFactory.InitClient(settings.StrikeApiKey);

            foreach (var order in orders)
            {
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
                    order.DepositId = resp.Id.ToString();
                    db.AddExchangeOrderLogs(order.Id, DbExchangeOrderLog.Events.DepositCreated, resp, resp.Id.ToString());
                }
                else
                {
                    order.State = DbExchangeOrder.States.Error;
                    db.AddExchangeOrderLogs(order.Id, DbExchangeOrderLog.Events.Error, resp);
                }

                await db.SaveChangesAsync(cancellationToken);
            }

            // check balance and if it increased execute purchase immediately
            await Task.Delay(10000, cancellationToken).WaitAsync(cancellationToken);

            var balanceResp = await strikeClient.Balances.GetBalances();
            var usdBalanceOnStrike = balanceResp.FirstOrDefault(a => a.Currency == Currency.Usd)?.Available ?? 0;

            // 
            var waitingOrders = db.ExchangeOrders
                .Where(order => order.StoreId == ppe.StoreId
                                && order.State == DbExchangeOrder.States.DepositWaiting
                                && (order.DelayUntil == null || order.DelayUntil < DateTimeOffset.UtcNow))
                .OrderBy(a => a.CreatedForDate)
                .ThenBy(a => a.Created)
                .ToList();
            Logs.PayServer.LogInformation("ExchangeOrderHeartbeatService: {0} orders waiting on deposit on Strike",
                waitingOrders.Count);

            foreach (var order in waitingOrders)
            {
                var depositingId = Guid.Parse(order.DepositId);
                var resp = await strikeClient.Deposits.FindDeposit(depositingId);

                if (!resp.IsSuccessStatusCode)
                {
                    order.State = DbExchangeOrder.States.Error;
                    db.AddExchangeOrderLogs(order.Id, DbExchangeOrderLog.Events.Error, resp);
                    await db.SaveChangesAsync(cancellationToken);

                    // exiting the loop
                    continue;
                }

                if (resp.State == DepositState.Completed)
                {
                    await ExecuteConversionOrder(db, order, strikeClient, cancellationToken);
                }
                else if (resp.State == DepositState.Pending)
                {
                    // sometimes deposits are in pending state for a while, but there is available balance, proceed
                    if (usdBalanceOnStrike > order.Amount)
                        if (await ExecuteConversionOrder(db, order, strikeClient, cancellationToken))
                            usdBalanceOnStrike -= order.Amount;
                }
                else
                {
                    // Failed and Reversed deposits will also end up here
                    // TODO: If deposit failed, initiate it again

                    order.State = DbExchangeOrder.States.Error;
                    db.AddExchangeOrderLogs(order.Id, DbExchangeOrderLog.Events.Error, resp);
                    await db.SaveChangesAsync(cancellationToken);
                }
            }

            await UpdateStrikeCache(db, ppe.StoreId, strikeClient, cancellationToken);
        }
        catch (Exception ex)
        {
            Logs.PayServer.LogError("ExchangeOrderHeartbeatService: Error during PeriodProcessEventWork method execution {0}", ex);
        }
    }

    public static async Task UpdateStrikeCache(PluginDbContext db, string storeId, StrikeClient strikeClient,
        CancellationToken cancellationToken)
    {
        var strikeBalances = await strikeClient.Balances.GetBalances();
        if (strikeBalances.IsSuccessStatusCode)
        {
            db.SettingAddOrUpdate(storeId, DbSettingKeys.StrikeBalances, strikeBalances);
            await db.SaveChangesAsync(cancellationToken);
        }

        var exchangeRates = await strikeClient.Rates.GetRatesTicker();
        if (exchangeRates.IsSuccessStatusCode)
        {
            db.SettingAddOrUpdate(storeId, DbSettingKeys.StrikeExchangeRates, exchangeRates);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public static async Task<bool> ExecuteConversionOrder(PluginDbContext db, DbExchangeOrder order,
        StrikeClient strikeClient, CancellationToken cancellationToken)
    {
        var req = new CurrencyExchangeQuoteReq
        {
            Buy = Currency.Btc,
            Sell = Currency.Usd,
            Amount = new MoneyWithFee
            {
                Currency = Currency.Usd,
                Amount = order.Amount,
                FeePolicy = FeePolicy.Inclusive
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
            await db.SaveChangesAsync(cancellationToken);

            // exiting the loop
            return false;
        }

        var executeQuoteResp = await strikeClient.CurrencyExchanges.ExecuteQuote(exchangeResp.Id);
        if (!executeQuoteResp.IsSuccessStatusCode)
        {
            order.State = DbExchangeOrder.States.Error;
            db.AddExchangeOrderLogs(order.Id, DbExchangeOrderLog.Events.Error, executeQuoteResp);
            await db.SaveChangesAsync(cancellationToken);

            // exiting the loop
            return false;
        }

        db.AddExchangeOrderLogs(order.Id, DbExchangeOrderLog.Events.ExecutingExchange, exchangeResp, exchangeResp.Id.ToString());
        order.TargetAmount = exchangeResp.Target.Amount;
        order.ConversionRate = Math.Round(1 / exchangeResp.ConversionRate.Amount, 2);
        order.State = DbExchangeOrder.States.Completed;
        db.AddExchangeOrderLogs(order.Id, DbExchangeOrderLog.Events.ExchangeExecuted, executeQuoteResp);
        await db.SaveChangesAsync(cancellationToken);

        return true;
    }

    public class PeriodProcessEvent
    {
        public string StoreId { get; set; }
        public SettingsViewModel Setting { get; set; }
    }
}
