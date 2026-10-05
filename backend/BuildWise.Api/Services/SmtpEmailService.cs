using System.Net;
using System.Net.Mail;

namespace BuildWise.Api.Services;

/// <summary>
/// Third-party integration (spec §11): sends procurement notification emails
/// through any standard SMTP provider (e.g. Gmail with an app password, or a
/// free sandbox like Mailtrap/Ethereal). Configuration lives under "Smtp" in
/// appsettings — credentials are never hard-coded and should be supplied via
/// user-secrets or environment variables in real use.
///
/// If no host is configured, sends are logged instead of attempted, so the
/// rest of the procurement workflow never breaks in an environment without
/// SMTP credentials (e.g. a bare CI runner or a fresh clone).
/// </summary>
public class SmtpEmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(IConfiguration config, ILogger<SmtpEmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(toEmail))
            return;

        var section = _config.GetSection("Smtp");
        var host = section["Host"];

        if (string.IsNullOrWhiteSpace(host))
        {
            _logger.LogInformation(
                "SMTP not configured — logging email instead of sending. To: {To} | Subject: {Subject}\n{Body}",
                toEmail, subject, body);
            return;
        }

        var port = int.TryParse(section["Port"], out var p) ? p : 587;
        var enableSsl = !bool.TryParse(section["EnableSsl"], out var ssl) || ssl;
        var username = section["Username"];
        var password = section["Password"];
        var fromAddress = section["FromAddress"] ?? username ?? "noreply@buildwise.local";
        var fromName = section["FromName"] ?? "BuildWise Procurement";
        var timeoutMs = int.TryParse(section["TimeoutMs"], out var t) ? t : 8000;

        using var message = new MailMessage
        {
            From = new MailAddress(fromAddress, fromName),
            Subject = subject,
            Body = body,
            IsBodyHtml = false
        };
        message.To.Add(toEmail);

        using var client = new SmtpClient(host, port)
        {
            EnableSsl = enableSsl,
            Timeout = timeoutMs
        };
        if (!string.IsNullOrWhiteSpace(username))
        {
            client.Credentials = new NetworkCredential(username, password);
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeoutMs);
            await client.SendMailAsync(message, cts.Token);
            _logger.LogInformation("Notification email sent to {To}: {Subject}", toEmail, subject);
        }
        catch (Exception ex)
        {
            // Never let a notification failure break the procurement workflow that triggered it.
            _logger.LogWarning(ex, "Failed to send notification email to {To}", toEmail);
        }
    }
}
