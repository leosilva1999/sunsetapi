using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using Sunset.Application.Exceptions;
using Sunset.Application.Interfaces;

namespace Sunset.Infrastructure.Email;

public class SmtpEmailSender(IOptions<SmtpOptions> options) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        var smtp = options.Value;

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(smtp.FromName, smtp.FromAddress));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(smtp.Host, smtp.Port, smtp.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None, cancellationToken);

            // Mailpit (dev) accepts unauthenticated connections - only authenticate when
            // credentials are actually configured, same guard S3's client creation uses for
            // LocalStack vs. real AWS credentials.
            if (!string.IsNullOrWhiteSpace(smtp.Username))
                await client.AuthenticateAsync(smtp.Username, smtp.Password ?? string.Empty, cancellationToken);

            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ExternalServiceException($"Failed to send email via SMTP: {ex.Message}");
        }
    }
}
