using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Logging;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data.Models;
using BTCPayServer.Services.Mails;
using BTCPayServer.Services;
using BTCPayServer.Services.Stores;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Services;

public class EmailService(EmailSenderFactory emailSender, Logs logs, 
    StoreRepository storeRepo, PayrollPluginDbContextFactory pluginDbContextFactory)
{
    public async Task SendBulkEmail(IEnumerable<EmailRecipient> recipients)
    {
        var settings = await (await emailSender.GetEmailSender()).GetEmailSettings();
        if (!settings.IsComplete())
            return;
        
        var client = await settings.CreateSmtpClient();
        try
        {
            foreach (var recipient in recipients)
            {
                try
                {
                    var message = new MimeMessage();
                    message.From.Add(MailboxAddress.Parse(settings.From));
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
        var emailRecipients = new List<EmailRecipient>();

        foreach (var storeGroup in invoicesByStore)
        {
            var setting = await pluginDbContextFactory.GetSettingAsync(storeGroup.Key);
            if (setting?.EmailOnInvoicePaid != true)
                continue;

            foreach (var invoice in storeGroup)
            {
                var storeName = (await storeRepo.FindStore(invoice.User.StoreId))?.StoreName;
                emailRecipients.Add(new EmailRecipient
                {
                    Address = InternetAddress.Parse(invoice.User.Email),
                    Subject = setting.EmailOnInvoicePaidSubject,
                    MessageText = setting.EmailOnInvoicePaidBody
                        .Replace("{Name}", invoice.User.Name)
                        .Replace("{StoreName}", storeName)
                        .Replace("{CreatedAt}", invoice.CreatedAt.ToString("MMM dd, yyyy h:mm tt zzz"))
                        .Replace("{PaidAt}", invoice.PaidAt?.ToString("MMM dd, yyyy h:mm tt zzz"))
                        .Replace("{VendorPayPublicLink}", $"{setting.VendorPayPublicLink}")
                });
            }
        }

        if (emailRecipients.Any())
        {
            await SendBulkEmail(emailRecipients);
        }
    }

    public class EmailRecipient
    {
        public InternetAddress Address { get; set; }
        public string Subject { get; set; }
        public string MessageText { get; set; }
    }
}
