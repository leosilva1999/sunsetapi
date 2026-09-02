namespace Sunset.Infrastructure.Security;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Secret { get; init; } = null!;
    public string Issuer { get; init; } = null!;
    public string Audience { get; init; } = null!;
    public int AccessTokenMinutes { get; init; } = 15;
    public int RefreshTokenDays { get; init; } = 30;
}
