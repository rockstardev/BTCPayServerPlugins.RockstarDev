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
using System.IO;
using System.Reflection;

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

    public async Task SendUserInvitationEmailEmail(InvitationEmailModel model)
    {
        var settings = await (await emailSender.GetEmailSender(model.StoreId)).GetEmailSettings();
        if (!settings.IsComplete())
            return;
        
        var templateContent = GetEmbeddedResourceContent("Templates.InvitationEmail.cshtml");
        string emailBody = templateContent
                            .Replace("@Model.Name", model.UserName)
                            .Replace("@Model.StoreName", model.StoreName)
                            .Replace("@Model.VendorPayRegisterLink", model.VendorPayRegisterLink);
        var client = await settings.CreateSmtpClient();
        var clientMessage = new MimeMessage
        {
            Subject = model.Subject,
            Body = new BodyBuilder
            {
                HtmlBody = emailBody,
                TextBody = StripHtml(emailBody)
            }.ToMessageBody()
        };
        clientMessage.From.Add(MailboxAddress.Parse(settings.From));
        clientMessage.To.Add(InternetAddress.Parse(model.UserEmail));
        await client.SendAsync(clientMessage);
        await client.DisconnectAsync(true);
    }

    private string StripHtml(string html)
    {
        return System.Text.RegularExpressions.Regex.Replace(html, "<.*?>", string.Empty)
            .Replace("&nbsp;", " ")
            .Trim();
    }

    public string GetEmbeddedResourceContent(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var fullResourceName = assembly.GetManifestResourceNames()
                                       .FirstOrDefault(r => r.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));

        if (fullResourceName == null)
        {
            throw new FileNotFoundException($"Resource '{resourceName}' not found in assembly.");
        }
        using var stream = assembly.GetManifestResourceStream(fullResourceName);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public class EmailRecipient
    {
        public InternetAddress Address { get; set; }
        public string Subject { get; set; }
        public string MessageText { get; set; }
    }
}
