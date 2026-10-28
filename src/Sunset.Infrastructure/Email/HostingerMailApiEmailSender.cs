using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sunset.Application.Exceptions;
using Sunset.Application.Interfaces;

namespace Sunset.Infrastructure.Email;

/// <summary>
/// Sends through the Hostinger Mail API (HTTPS) - the alternative to <see cref="SmtpEmailSender"/>
/// for hosts that block outbound SMTP ports (Render's free tier blocks 25/465/587).
/// </summary>
public class HostingerMailApiEmailSender(HttpClient httpClient, IOptions<HostingerMailOptions> options, ILogger<HostingerMailApiEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;

        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/v1/mailboxes/{Uri.EscapeDataString(settings.MailboxResourceId)}/send")
        {
            Content = JsonContent.Create(new SendPayload([to], settings.DisplayName, subject, htmlBody)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // The response body is read only to explain the failure; recipient and message
                // content never reach the log (personal data).
                var detail = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new HttpRequestException($"Hostinger Mail API returned {(int)response.StatusCode}: {Truncate(detail)}");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Hostinger Mail API send failed (subject: {Subject})", subject);
            throw new ExternalServiceException($"Failed to send email via Hostinger Mail API: {ex.Message}", ex);
        }
    }

    private static string Truncate(string value) => value.Length <= 300 ? value : value[..300];

    // Field names follow the Hostinger SDK's V1SendRequest model (camelCase on the wire).
    private sealed record SendPayload(string[] To, string? DisplayName, string Subject, string Html);
}
