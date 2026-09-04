using System.Net;
using System.Net.Mail;
using BulkReversal.Application.Common.Interfaces;
using BulkReversal.Application.Common.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BulkReversal.Infrastructure.Email;

/// <summary>
/// Best-effort SMTP notification sender for the approval-routing workflow (batch submitted -&gt;
/// notify authorizers; batch approved/rejected -&gt; notify the initiator). Never throws: a
/// notification failure must not roll back or block the underlying business action, so every
/// failure is logged and swallowed here rather than propagated to the caller.
/// </summary>
public class SmtpEmailService : IEmailService
{
    private readonly EmailOptions _options;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(IOptions<EmailOptions> options, ILogger<SmtpEmailService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(IReadOnlyCollection<string> toAddresses, string subject, string htmlBody, CancellationToken ct = default)
    {
        var recipients = toAddresses.Where(a => !string.IsNullOrWhiteSpace(a)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (recipients.Count == 0)
        {
            _logger.LogWarning("Email '{Subject}' was not sent: no recipient addresses were resolved.", subject);
            return;
        }

        if (!_options.Enabled)
        {
            _logger.LogInformation(
                "Email notifications are disabled (Email:Enabled=false); would have sent '{Subject}' to {Recipients}.",
                subject, string.Join(", ", recipients));
            return;
        }

        try
        {
            using var message = new MailMessage
            {
                From = new MailAddress(_options.FromAddress, _options.FromName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };

            foreach (var recipient in recipients)
            {
                message.To.Add(recipient);
            }

            using var client = new SmtpClient(_options.SmtpHost, _options.SmtpPort)
            {
                EnableSsl = _options.EnableSsl
            };

            if (!string.IsNullOrWhiteSpace(_options.Username))
            {
                client.Credentials = new NetworkCredential(_options.Username, _options.Password);
            }

            await client.SendMailAsync(message, ct);

            _logger.LogInformation("Email '{Subject}' sent to {Recipients}.", subject, string.Join(", ", recipients));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email '{Subject}' to {Recipients}.", subject, string.Join(", ", recipients));
        }
    }
}
