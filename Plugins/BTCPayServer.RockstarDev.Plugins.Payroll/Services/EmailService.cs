using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.RockstarDev.Plugins.Payroll.ViewModels;
using BTCPayServer.Services;
using BTCPayServer.Services.Mails;
using MimeKit;
using System.Reflection;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Services;

public class EmailService
{
    private readonly SettingsRepository _settingsRepository;
    public EmailService(SettingsRepository settingsRepository)
    {
        _settingsRepository = settingsRepository;
    }

    public async Task SendUserInvitationEmailEmail(string email, string recipientName, string storeName, string invitationUrl)
    {
        var settings = await _settingsRepository.GetSettingAsync<EmailSettings>();
        var model = new InvitationEmailModel
        {
            Store = storeName,
            RegistrationLink = invitationUrl,
            UserName = recipientName
        };

        // Load the template from embedded resources
        var templateContent = GetEmbeddedResourceContent("Templates.InvitationEmail.cshtml");

        string emailBody = GenerateEmailContent(templateContent, model);
        var client = await settings.CreateSmtpClient();
        var clientMessage = new MimeMessage
        {
            Subject = "You've got an invite",
            Body = new BodyBuilder
            {
                HtmlBody = emailBody,
                TextBody = StripHtml(emailBody)
            }.ToMessageBody()
        };
        clientMessage.From.Add(MailboxAddress.Parse(settings.From));
        clientMessage.To.Add(InternetAddress.Parse(email));
        await client.SendAsync(clientMessage);
        await client.DisconnectAsync(true);
    }

    public static string GetEmbeddedResourceContent(string resourceName)
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

    public string GenerateEmailContent(string templateContent, InvitationEmailModel model)
    {
        return templateContent
            .Replace("@Model.UserName", model.UserName)
            .Replace("@Model.Store", model.Store)
            .Replace("@Model.RegistrationLink", model.RegistrationLink);

    }

    private string StripHtml(string html)
    {
        return System.Text.RegularExpressions.Regex.Replace(html, "<.*?>", string.Empty)
            .Replace("&nbsp;", " ")
            .Trim();
    }
}
