namespace Sunset.Infrastructure.Email;

public class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string Host { get; init; } = null!;
    public int Port { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
    public string FromAddress { get; init; } = null!;
    public string FromName { get; init; } = null!;
    public bool UseStartTls { get; init; }
}
