using Google.Apis.Auth;
using Microsoft.Extensions.Options;
using Sunset.Application.Interfaces;

namespace Sunset.Infrastructure.Security;

public class GoogleIdTokenVerifier(IOptions<GoogleAuthOptions> options) : IGoogleIdTokenVerifier
{
    private readonly GoogleAuthOptions _options = options.Value;

    public async Task<GoogleUserInfo?> VerifyAsync(string idToken, CancellationToken cancellationToken = default)
    {
        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = [_options.ClientId],
            };

            // ValidateAsync confere assinatura, expiração, issuer e audience contra o
            // Client ID configurado - lança se qualquer um desses falhar.
            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, settings);

            return new GoogleUserInfo(payload.Email, payload.EmailVerified, payload.Name);
        }
        catch (InvalidJwtException)
        {
            return null;
        }
    }
}
