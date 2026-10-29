namespace Sunset.Application.Interfaces;

public sealed record ActiveBan(string Reason, DateTime? Until);

/// <summary>
/// Answers "is this user banned right now?" for every authenticated request (the JWT is
/// stateless, so a ban would otherwise only bite once the access token expires). Implemented
/// with a short cache; <see cref="Invalidate"/> makes ban/unban take effect immediately.
/// </summary>
public interface IBanStatusService
{
    Task<ActiveBan?> GetActiveBanAsync(Guid userId, CancellationToken cancellationToken = default);
    void Invalidate(Guid userId);
}
