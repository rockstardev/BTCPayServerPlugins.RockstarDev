using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Logging;
using BTCPayServer.Plugins.Emails.Services;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data.Models;
using BTCPayServer.Services.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Services;

public class EmailService(
    EmailSenderFactory emailSenderFactory,
    Logs logs,
    StoreRepository storeRepo,
    PluginDbContextFactory pluginDbContextFactory)
{
    public async Task<bool> IsEmailSettingsConfigured(string storeId)
    {
        var emailSender = await emailSenderFactory.GetEmailSender(storeId);
        return (await emailSender.GetEmailSettings() ?? new EmailSettings()).IsComplete();
    }

    private async Task SendBulkEmail(string storeId, IEnumerable<EmailRecipient> recipients)
    {
        var emailSettings = await (await emailSenderFactory.GetEmailSender(storeId)).GetEmailSettings();
        if (emailSettings?.IsComplete() != true)
            return;

        var client = await emailSettings.CreateSmtpClient();
        try
        {
            foreach (var recipient in recipients)
                try
                {
                    var message = new MimeMessage();
                    message.From.Add(MailboxAddress.Parse(emailSettings.From));
                    message.To.Add(recipient.Address);
                    message.Subject = recipient.Subject;
                    message.Body = new TextPart("plain") { Text = recipient.MessageText };
                    await client.SendAsync(message);
                }
                catch (Exception ex)
                {
                    logs.PayServer.LogError(ex, $"Error sending email to: {recipient.Address}");
                }
        }
        finally
        {
            await client.DisconnectAsync(true);
        }
    }

    public async Task SendSuccessfulInvoicePaymentEmail(List<PayrollInvoice> invoices)
    {
        if (!invoices.Any())
            return;

        var invoicesByStore = invoices.GroupBy(i => i.User.StoreId);

        foreach (var storeGroup in invoicesByStore)
        {
            var emailRecipients = new List<EmailRecipient>();
            var setting = await pluginDbContextFactory.GetSettingAsync(storeGroup.Key);
            if (setting?.EmailOnInvoicePaid != true)
                continue;

            var storeName = (await storeRepo.FindStore(storeGroup.Key))?.StoreName;

            foreach (var invoice in storeGroup)
                emailRecipients.Add(new EmailRecipient
                {
                    Address = InternetAddress.Parse(invoice.User.Email),
                    Subject = setting.EmailOnInvoicePaidSubject,
                    MessageText = setting.EmailOnInvoicePaidBody
                        .Replace("{Name}", invoice.User.Name)
                        .Replace("{StoreName}", storeName)
                        .Replace("{CreatedAt}", invoice.CreatedAt.ToString("MMM dd, yyyy h:mm tt zzz"))
                        .Replace("{PaidAt}", invoice.PaidAt?.ToString("MMM dd, yyyy h:mm tt zzz"))
                        .Replace("{VendorPayPublicLink}", setting.VendorPayPublicLink)
                });

            if (emailRecipients.Any()) await SendBulkEmail(storeGroup.Key, emailRecipients);
        }
    }

    public async Task SendUserInvitationEmail(PayrollUser model, string subject, string body, string vendorPayRegisterationLink)
    {
        var emailSettings = await (await emailSenderFactory.GetEmailSender(model.StoreId)).GetEmailSettings();
        if (emailSettings?.IsComplete() != true)
            return;

        var storeName = (await storeRepo.FindStore(model.StoreId))?.StoreName;
        var recipient = new EmailRecipient
        {
            Address = InternetAddress.Parse(model.Email),
            Subject = subject,
            MessageText = body
                .Replace("{Name}", model.Name)
                .Replace("{StoreName}", storeName)
                .Replace("{VendorPayRegisterLink}", vendorPayRegisterationLink)
        };
        var emailRecipients = new List<EmailRecipient> { recipient };
        await SendBulkEmail(model.StoreId, emailRecipients);
    }

    public async Task<bool> SendInvoiceEmailReminder(PayrollUser model, string subject, string body)
    {
        var emailSettings = await (await emailSenderFactory.GetEmailSender(model.StoreId)).GetEmailSettings();
        if (emailSettings?.IsComplete() != true)
            return false;

        var store = await storeRepo.FindStore(model.StoreId);
        var storeName = store.StoreName;

        var settings = await pluginDbContextFactory.GetSettingAsync(store.Id);
        if (settings?.EmailReminders != true)
            return false;

        var recipient = new EmailRecipient
        {
            Address = InternetAddress.Parse(model.Email),
            Subject = subject,
            MessageText = body
                .Replace("{Name}", model.Name)
                .Replace("{StoreName}", storeName)
                .Replace("{VendorPayPublicLink}", settings.VendorPayPublicLink)
        };
        var emailRecipients = new List<EmailRecipient> { recipient };
        await SendBulkEmail(model.StoreId, emailRecipients);
        return true;
    }

    public async Task SendAdminNotificationOnInvoiceUpload(string storeId, PayrollInvoice invoice)
    {
        try
        {
            var settings = await pluginDbContextFactory.GetSettingAsync(storeId);
            if (settings?.EmailAdminOnInvoiceUploaded != true || 
                string.IsNullOrWhiteSpace(settings.EmailAdminOnInvoiceUploadedAddress))
                return;
            
            var emailSettings = await (await emailSenderFactory.GetEmailSender(storeId)).GetEmailSettings();
            if (emailSettings?.IsComplete() != true)
                return;

            // Ensure user is loaded
            if (invoice.User == null)
            {
                await using var ctx = pluginDbContextFactory.CreateContext();
                invoice.User = await ctx.PayrollUsers.FindAsync(invoice.UserId);
            
                if (invoice.User == null)
                {
                    logs.PayServer.LogWarning($"Could not find user {invoice.UserId} for invoice {invoice.Id} notification");
                    return;
                }
            }

            // Parse comma-separated email addresses
            var emailAddresses = settings.EmailAdminOnInvoiceUploadedAddress
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(e => e.Trim())
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .ToList();

            if (!emailAddresses.Any())
                return;

            var messageText = settings.EmailAdminOnInvoiceUploadedBody
                .Replace("{VendorName}", invoice.User.Name)
                .Replace("{VendorEmail}", invoice.User.Email)
                .Replace("{InvoiceId}", invoice.Id)
                .Replace("{Amount}", invoice.Amount.ToString())
                .Replace("{Currency}", invoice.Currency)
                .Replace("{Destination}", invoice.Destination);

            var emailRecipients = emailAddresses.Select(email => new EmailRecipient
            {
                Address = InternetAddress.Parse(email),
                Subject = settings.EmailAdminOnInvoiceUploadedSubject,
                MessageText = messageText
            }).ToList();

            await SendBulkEmail(storeId, emailRecipients);
        }
        catch (Exception ex)
        {
            logs.PayServer.LogError(ex, $"Error sending admin notification for invoice upload {invoice.Id}");
        }
    }

    public async Task SendAdminNotificationOnInvoiceDelete(string storeId, PayrollInvoice invoice, string vendorName, string vendorEmail)
    {
        try
        {
            var settings = await pluginDbContextFactory.GetSettingAsync(storeId);
            if (settings?.EmailAdminOnInvoiceDeleted != true || 
                string.IsNullOrWhiteSpace(settings.EmailAdminOnInvoiceDeletedAddress))
                return;

            var emailSettings = await (await emailSenderFactory.GetEmailSender(storeId)).GetEmailSettings();
            if (emailSettings?.IsComplete() != true)
                return;

            // Parse comma-separated email addresses
            var emailAddresses = settings.EmailAdminOnInvoiceDeletedAddress
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(e => e.Trim())
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .ToList();

            if (!emailAddresses.Any())
                return;

            var messageText = settings.EmailAdminOnInvoiceDeletedBody
                .Replace("{VendorName}", vendorName)
                .Replace("{VendorEmail}", vendorEmail ?? "unknown")
                .Replace("{InvoiceId}", invoice.Id)
                .Replace("{Amount}", invoice.Amount.ToString())
                .Replace("{Currency}", invoice.Currency)
                .Replace("{Destination}", invoice.Destination);

            var emailRecipients = emailAddresses.Select(email => new EmailRecipient
            {
                Address = InternetAddress.Parse(email),
                Subject = settings.EmailAdminOnInvoiceDeletedSubject,
                MessageText = messageText
            }).ToList();

            await SendBulkEmail(storeId, emailRecipients);
        }
        catch (Exception ex)
        {
            logs.PayServer.LogError(ex, $"Error sending admin notification for invoice deletion {invoice.Id}");
        }
    }
    public class EmailRecipient
    {
        public InternetAddress Address { get; set; }
        public string Subject { get; set; }
        public string MessageText { get; set; }
    }
}
