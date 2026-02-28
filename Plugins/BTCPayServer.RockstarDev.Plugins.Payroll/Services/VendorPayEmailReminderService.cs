using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Data;
using BTCPayServer.RockstarDev.Plugins.VendorPay.Logic;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BTCPayServer.RockstarDev.Plugins.VendorPay.Services;

public class VendorPayEmailReminderService(
    EmailService emailService,
    EventAggregator eventAggregator,
    PluginDbContextFactory pluginDbContextFactory,
    Logs logs)
    : EventHostedServiceBase(eventAggregator, logs), IPeriodicTask
{
    public async Task Do(CancellationToken cancellationToken)
    {
        try
        {
            await using var db = pluginDbContextFactory.CreateContext();

            var stores = db.PayrollSettings.Select(a => a.StoreId).ToList();

            foreach (var storeId in stores)
            {
                if (!await emailService.IsEmailSettingsConfigured(storeId))
                    continue;

                var settings = await db.GetSettingAsync(storeId);
                if (settings == null || !settings.EmailReminders)
                    continue;

                PushEvent(new PeriodProcessEvent { StoreId = storeId, Setting = settings });
            }
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01")
        {
            Logs.PayServer.LogInformation("Skipping task: PayrollSettings table not created yet.");
        }
    }

    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        if (evt is PeriodProcessEvent sequentialExecute)
            await HandleEmailReminders(sequentialExecute.StoreId, sequentialExecute.Setting);

        await base.ProcessEvent(evt, cancellationToken);
    }

    private async Task HandleEmailReminders(string storeId, VendorPayStoreSetting settings)
    {
        var shouldUpdateDb = false;

        await using var ctx = pluginDbContextFactory.CreateContext();
        var todayDate = DateTime.UtcNow.Date;
        var threeDaysAgo = todayDate.AddDays(-3);
        var usersToEmailCandidates = ctx.PayrollUsers.Where(a =>
                a.StoreId == storeId && a.State == VendorPayUserState.Active && !string.IsNullOrEmpty(a.EmailReminder) &&
                !a.PayrollInvoices.Any(i => i.UserId == a.Id && i.CreatedAt >= threeDaysAgo) &&
                (!a.LastReminderSent.HasValue || a.LastReminderSent.Value.Date != todayDate))
            .ToList();

        foreach (var user in usersToEmailCandidates)
        {
            var reminders = user.EmailReminder.Split(',').Select(int.Parse).ToList();
            var lastDayOfMonth = DateTime.DaysInMonth(todayDate.Year, todayDate.Month);
            var emailOnLastDay = reminders.Contains(31) && todayDate.Day == lastDayOfMonth;
            if (reminders.Contains(todayDate.Day) || emailOnLastDay)
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

        if (shouldUpdateDb)
        {
            ctx.UpdateRange(usersToEmailCandidates);
            await ctx.SaveChangesAsync();
        }
    }

    public class PeriodProcessEvent
    {
        public string StoreId { get; set; }
        public VendorPayStoreSetting Setting { get; set; }
    }
}
