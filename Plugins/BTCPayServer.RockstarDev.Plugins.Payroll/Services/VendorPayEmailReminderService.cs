using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data.Models;
using BTCPayServer.RockstarDev.Plugins.Payroll.Logic;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Services;

public class VendorPayEmailReminderService(
    EmailService emailService,
    EventAggregator eventAggregator,
    PluginDbContextFactory pluginDbContextFactory,
    Logs logs)
    : EventHostedServiceBase(eventAggregator, logs), IPeriodicTask
{
    public async Task Do(CancellationToken cancellationToken)
    {
        await using var db = pluginDbContextFactory.CreateContext();
        var stores = db.PayrollSettings.Select(a=>a.StoreId).ToList();
        foreach (var storeId in stores)
        {
            if (await emailService.IsEmailSettingsConfigured(storeId) == false)
                continue;
            
            var settings = await db.GetSettingAsync(storeId);
            if (settings == null || !settings.EmailReminders)
                continue;

            PushEvent(new PeriodProcessEvent { StoreId = storeId, Setting = settings });
        }
    }

    public class PeriodProcessEvent
    {
        public string StoreId { get; set; }
        public PayrollStoreSetting Setting { get; set; }
    }

    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        if (evt is PeriodProcessEvent sequentialExecute)
        {
            await HandleEmailReminders(sequentialExecute.StoreId, sequentialExecute.Setting);
        }

        await base.ProcessEvent(evt, cancellationToken);
    }

    private async Task HandleEmailReminders(string storeId, PayrollStoreSetting settings)
    {
        bool shouldUpdateDb = false;

        await using var ctx = pluginDbContextFactory.CreateContext();
        List<PayrollUser> activeUsers = ctx.PayrollUsers.Where(a => 
            a.StoreId == storeId && a.State == PayrollUserState.Active && a.EmailReminder != null && a.EmailReminder != "")
            .ToList();

        DateTime todayDate = DateTime.UtcNow.Date;
        foreach (var user in activeUsers)
        {
            if (user.LastReminderSent.HasValue && user.LastReminderSent.Value.Date == todayDate)
                continue;

            List<int> reminders = user.EmailReminder.Split(',').Select(int.Parse).ToList();
            var lastDayOfMonth = DateTime.DaysInMonth(todayDate.Year, todayDate.Month);
            var emailOnLastDay = reminders.Contains(31) && todayDate.Day == lastDayOfMonth;
            if (reminders.Contains(todayDate.Day) || emailOnLastDay)
            {
                try
                {
                    var emailSent = await emailService.SendInvoiceEmailReminder(user, settings.EmailRemindersSubject,
                        settings.EmailRemindersBody);
                    if (emailSent)
                    {
                        user.LastReminderSent = todayDate;
                        shouldUpdateDb = true;
                    }
                }
                catch (Exception ex)
                {
                    Logs.PayServer.LogError("VendorPay: HandleEmailReminders fail: {0} ", ex);
                }
            }
        }
        if (shouldUpdateDb)
        {
            ctx.UpdateRange(activeUsers);
            await ctx.SaveChangesAsync();
        }
    }
}