using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Logging;
using BTCPayServer.RockstarDev.Plugins.Subscriptions.Data;
using BTCPayServer.RockstarDev.Plugins.Subscriptions.Data.Models;
using BTCPayServer.Services.Mails;
using BTCPayServer.Services.Stores;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace BTCPayServer.RockstarDev.Plugins.Subscriptions.Services;

public class EmailService(EmailSenderFactory emailSender, Logs logs, 
    StoreRepository storeRepo, PluginDbContextFactory dbContextFactory)
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

    // public async Task SendSuccessfulInvoicePaymentEmail(List<Subscription> list)
    // {
    //     if (!list.Any())
    //         return;
    //
    //     var invoicesByStore = list.GroupBy(i => i.Customer.StoreId);
    //
    //     foreach (var storeGroup in invoicesByStore)
    //     {
    //         var emailRecipients = new List<EmailRecipient>();
    //         var setting = await dbContextFactory.GetSettingAsync(storeGroup.Key);
    //         if (setting?.EmailOnInvoicePaid != true)
    //             continue;
    //         
    //         var storeName = (await storeRepo.FindStore(storeGroup.Key))?.StoreName;
    //
    //         foreach (var invoice in storeGroup)
    //         {
    //             emailRecipients.Add(new EmailRecipient
    //             {
    //                 Address = InternetAddress.Parse(invoice.Customer.Email),
    //                 Subject = setting.EmailOnInvoicePaidSubject,
    //                 MessageText = setting.EmailOnInvoicePaidBody
    //                     .Replace("{Name}", invoice.Customer.Name)
    //                     .Replace("{StoreName}", storeName)
    //                     .Replace("{Created}", invoice.Created.ToString("MMM dd, yyyy h:mm tt zzz"))
    //                     .Replace("{Expires}", invoice.Expires.ToString("MMM dd, yyyy h:mm tt zzz"))
    //             });
    //         }
    //
    //         if (emailRecipients.Any())
    //         {
    //             await SendBulkEmail(storeGroup.Key, emailRecipients);
    //         }
    //     }
    // }

    public class EmailRecipient
    {
        public InternetAddress Address { get; set; }
        public string Subject { get; set; }
        public string MessageText { get; set; }
    }

    public async Task SendEmail(string storeId, EmailRecipient recipient)
    {
        var settings = await (await emailSender.GetEmailSender(storeId)).GetEmailSettings();
        if (!settings.IsComplete())
            return;
        
        var client = await settings.CreateSmtpClient();

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
        finally
        {
            await client.DisconnectAsync(true);
        }
    }
}
