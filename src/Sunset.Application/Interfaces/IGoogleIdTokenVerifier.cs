namespace Sunset.Application.Interfaces;

public interface IGoogleIdTokenVerifier
{
    // Retorna null se o token for inválido, expirado, ou tiver audience diferente do
    // nosso Client ID - GoogleLoginAsync trata isso como credencial inválida (401).
    Task<GoogleUserInfo?> VerifyAsync(string idToken, CancellationToken cancellationToken = default);
}

public record GoogleUserInfo(string Email, bool EmailVerified, string Name);
