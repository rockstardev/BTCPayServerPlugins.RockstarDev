using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;

namespace BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.Services;

public class ExchangeOrderHeartbeatService(
    EventAggregator eventAggregator,
    Logs logs) : EventHostedServiceBase(eventAggregator, logs), IPeriodicTask
{
    protected override void SubscribeToEvents()
    {
        base.SubscribeToEvents();
    }

    public Task Do(CancellationToken cancellationToken)
    {
        PushEvent(new PeriodProcessEvent());
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