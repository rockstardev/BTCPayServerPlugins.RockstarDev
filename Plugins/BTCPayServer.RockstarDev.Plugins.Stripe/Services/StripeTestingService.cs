using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.RockstarDev.Plugins.Stripe.Services;

public class StripeTestingService(
    EventAggregator eventAggregator,
    Logs logs) : EventHostedServiceBase(eventAggregator, logs), IPeriodicTask
{
    public Task Do(CancellationToken cancellationToken)
    {
        Logs.PayServer.LogInformation($"{GetType().Name}: Do method invoked.");
        return Task.CompletedTask;
    }
}
