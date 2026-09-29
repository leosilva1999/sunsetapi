namespace Sunset.Infrastructure.Security;

public class GoogleAuthOptions
{
    public const string SectionName = "GoogleAuth";

    public string ClientId { get; init; } = null!;
}
