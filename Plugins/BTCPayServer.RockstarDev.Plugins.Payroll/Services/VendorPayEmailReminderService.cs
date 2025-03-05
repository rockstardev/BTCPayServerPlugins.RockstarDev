using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data.Models;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Services;


public class VendorPayEmailReminderService(
    EmailService emailService,
    EventAggregator eventAggregator,
    PayrollPluginDbContextFactory payrollPluginDbContextFactory,
    Logs logs)
    : EventHostedServiceBase(eventAggregator, logs)
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {

        await base.StartAsync(cancellationToken);
        _ = ScheduleChecks();
    }

    private CancellationTokenSource _checkTcs = new();

    private async Task ScheduleChecks()
    {
        while (!CancellationToken.IsCancellationRequested)
        {
            try
            {
                var tcs = new TaskCompletionSource<object>();

                PushEvent(new SequentialExecute(async () =>
                {
                    await HandleEmailReminders();
                    return null;

                }, tcs));
                await tcs.Task;
            }
            catch (Exception e)
            {
                Logs.PayServer.LogError(e, "Error while checking email reminder subscriptions");
            }
            _checkTcs = new CancellationTokenSource();
            _checkTcs.CancelAfter(TimeSpan.FromHours(1));
            try
            {
                await Task.Delay(TimeSpan.FromHours(1),
                    CancellationTokenSource.CreateLinkedTokenSource(_checkTcs.Token, CancellationToken).Token);
            }
            catch (OperationCanceledException) { }
        }
    }

    protected override void SubscribeToEvents()
    {
        Subscribe<SequentialExecute>();
        base.SubscribeToEvents();
    }

    public record SequentialExecute(Func<Task<object>> Action, TaskCompletionSource<object> TaskCompletionSource);

    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        if (evt is SequentialExecute sequentialExecute)
        {
            var task = await sequentialExecute.Action();
            sequentialExecute.TaskCompletionSource.SetResult(task);
            return;
        }

        await base.ProcessEvent(evt, cancellationToken);
    }

    private async Task HandleEmailReminders()
    {
        bool shouldUpdateDb = false;

        await using var ctx = payrollPluginDbContextFactory.CreateContext();
        List<PayrollUser> activeUsers = ctx.PayrollUsers.Where(a => a.State == PayrollUserState.Active).ToList();

        DateTime todayDate = DateTime.UtcNow.Date;
        foreach (var user in activeUsers)
        {
            if (user.LastReminderSent.HasValue && user.LastReminderSent.Value.Date == todayDate)
                continue;

            var settings = await payrollPluginDbContextFactory.GetSettingAsync(user.StoreId);
            if (settings == null || !settings.EmailReminders || string.IsNullOrEmpty(user.EmailReminder))
                continue;

            List<int> reminders = user.EmailReminder.Split(',').Select(int.Parse).ToList();
            if (reminders.Contains(todayDate.Day))
            {
                try
                {
                    var emailSent = await emailService.SendInvoiceEmailReminder(user, settings.EmailRemindersSubject, settings.EmailRemindersBody);
                    if (emailSent)
                    {
                        user.LastReminderSent = todayDate;
                        shouldUpdateDb = true;
                    }
                }
                catch (Exception) { }
            }
        }
        if (shouldUpdateDb)
        {
            ctx.UpdateRange(activeUsers);
            await ctx.SaveChangesAsync();
        }
    }
}