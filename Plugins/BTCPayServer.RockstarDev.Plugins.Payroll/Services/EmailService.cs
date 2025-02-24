using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Logging;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data.Models;
using BTCPayServer.Services.Mails;
using BTCPayServer.Services.Stores;
using Microsoft.Extensions.Logging;
using MimeKit;
using BTCPayServer.RockstarDev.Plugins.Payroll.ViewModels;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Services;

public class EmailService(EmailSenderFactory emailSender, Logs logs, 
    StoreRepository storeRepo, PayrollPluginDbContextFactory pluginDbContextFactory)
{
    private async Task SendBulkEmail(string storeId, IEnumerable<EmailRecipient> recipients)
    {
        var settings = await (await emailSender.GetEmailSender(storeId)).GetEmailSettings();
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

        foreach (var storeGroup in invoicesByStore)
        {
            var emailRecipients = new List<EmailRecipient>();
            var setting = await pluginDbContextFactory.GetSettingAsync(storeGroup.Key);
            if (setting?.EmailOnInvoicePaid != true)
                continue;
            
            var storeName = (await storeRepo.FindStore(storeGroup.Key))?.StoreName;

            foreach (var invoice in storeGroup)
            {
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

            if (emailRecipients.Any())
            {
                await SendBulkEmail(storeGroup.Key, emailRecipients);
            }
        }
    }

    public async Task SendUserInvitationEmailEmail(PayrollUser model, string subject, string body, string vendorPayRegisterationLink)
    {
        var settings = await (await emailSender.GetEmailSender(model.StoreId)).GetEmailSettings();
        if (!settings.IsComplete())
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

    public class EmailRecipient
    {
        public InternetAddress Address { get; set; }
        public string Subject { get; set; }
        public string MessageText { get; set; }
    }
}
