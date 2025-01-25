using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Logging;
using BTCPayServer.Services.Mails;
using BTCPayServer.Services;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Services;

public class EmailService(SettingsRepository settingsRepository, Logs logs)
{
    public async Task SendEmail(IEnumerable<InternetAddress> toList, string subject, string messageText)
    {
        var settings = await settingsRepository.GetSettingAsync<EmailSettings>();
        if (!settings.IsComplete())
            return;
        var client = await settings.CreateSmtpClient();
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(settings.From));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = messageText };
        message.To.AddRange(toList);
        await client.SendAsync(message);
        await client.DisconnectAsync(true);
    }

    public async Task SendBulkEmail(IEnumerable<EmailRecipient> recipients)
    {
        var settings = await settingsRepository.GetSettingAsync<EmailSettings>();
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

    public class EmailRecipient
    {
        public InternetAddress Address { get; set; }
        public string Subject { get; set; }
        public string MessageText { get; set; }
    }
}
