using System.Security.Cryptography;
using System.Text;
using Sunset.Application.DTOs.Auth;
using Sunset.Application.DTOs.Users;
using Sunset.Application.Exceptions;
using Sunset.Application.Interfaces;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Domain.Entities;

namespace Sunset.Application.Services;

public class AuthService(
    IUserRepository userRepository,
    IRefreshTokenRepository refreshTokenRepository,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    IGoogleIdTokenVerifier googleIdTokenVerifier) : IAuthService
{
    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        if (await userRepository.ExistsByEmailAsync(request.Email, cancellationToken))
            throw new ConflictException("Email is already registered.");

        var passwordHash = passwordHasher.Hash(request.Password);
        var user = new User(request.Name, request.Email, passwordHash);

        await userRepository.AddAsync(user, cancellationToken);

        return await IssueTokensAsync(user, cancellationToken);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByEmailAsync(request.Email, cancellationToken);
        if (user is null || !passwordHasher.Verify(request.Password, user.PasswordHash))
            throw new UnauthorizedActionException("Invalid email or password.");

        return await IssueTokensAsync(user, cancellationToken);
    }

    public async Task<AuthResponse> GoogleLoginAsync(GoogleAuthRequest request, CancellationToken cancellationToken = default)
    {
        var googleUser = await googleIdTokenVerifier.VerifyAsync(request.IdToken, cancellationToken);
        if (googleUser is null || !googleUser.EmailVerified)
            throw new UnauthorizedActionException("Invalid Google credential.");

        // E-mail já verificado pelo Google - se já existe uma conta com esse e-mail (criada
        // por senha ou por um login Google anterior), só autentica como ela. Senão, cria uma
        // conta nova com uma senha aleatória inutilizável (mesmo padrão de UserService.Anonymize),
        // já que PasswordHash é obrigatório na entidade mas essa conta nunca terá senha própria.
        var user = await userRepository.GetByEmailAsync(googleUser.Email, cancellationToken);
        if (user is null)
        {
            var unusablePasswordHash = passwordHasher.Hash(Guid.NewGuid().ToString());
            user = new User(googleUser.Name, googleUser.Email, unusablePasswordHash);
            await userRepository.AddAsync(user, cancellationToken);
        }

        return await IssueTokensAsync(user, cancellationToken);
    }

    public async Task<AuthResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default)
    {
        var tokenHash = Hash(request.RefreshToken);
        var storedToken = await refreshTokenRepository.GetByTokenHashAsync(tokenHash, cancellationToken);
        if (storedToken is null || !storedToken.IsActive)
            throw new UnauthorizedActionException("Invalid or expired refresh token.");

        var user = storedToken.User;
        var response = await IssueTokensAsync(user, cancellationToken);

        storedToken.Revoke(Hash(response.RefreshToken));
        await refreshTokenRepository.SaveChangesAsync(cancellationToken);

        return response;
    }

    public async Task LogoutAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default)
    {
        var tokenHash = Hash(request.RefreshToken);
        var storedToken = await refreshTokenRepository.GetByTokenHashAsync(tokenHash, cancellationToken);
        if (storedToken is null || !storedToken.IsActive)
            return;

        storedToken.Revoke();
        await refreshTokenRepository.SaveChangesAsync(cancellationToken);
    }

    private async Task<AuthResponse> IssueTokensAsync(User user, CancellationToken cancellationToken)
    {
        var (accessToken, accessTokenExpiresAt) = tokenService.GenerateAccessToken(user);
        var (refreshToken, refreshTokenExpiresAt) = tokenService.GenerateRefreshToken();

        var refreshTokenEntity = new RefreshToken(user.Id, Hash(refreshToken), refreshTokenExpiresAt);
        await refreshTokenRepository.AddAsync(refreshTokenEntity, cancellationToken);

        return new AuthResponse(accessToken, refreshToken, accessTokenExpiresAt, user.ToResponse());
    }

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }
}
