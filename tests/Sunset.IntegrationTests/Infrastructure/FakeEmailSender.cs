using System.Collections.Concurrent;
using Sunset.Application.Interfaces;

namespace Sunset.IntegrationTests.Infrastructure;

/// <summary>
/// Replaces the real SMTP sender in tests (see <see cref="SunsetApiFactory"/>) - the integration
/// suite shouldn't depend on Mailpit running or on any network at all. Registered as a singleton
/// so the same instance captures emails across every request in the test run.
/// </summary>
public class FakeEmailSender : IEmailSender
{
    private readonly ConcurrentDictionary<string, string> _htmlBodiesByRecipient = new();

    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        _htmlBodiesByRecipient[to] = htmlBody;
        return Task.CompletedTask;
    }

    public bool TryGetSentBody(string to, out string htmlBody) =>
        _htmlBodiesByRecipient.TryGetValue(to, out htmlBody!);
}
