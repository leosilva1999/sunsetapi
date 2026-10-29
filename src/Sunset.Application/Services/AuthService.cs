using System.Security.Cryptography;
using System.Text;
using Sunset.Application.Common;
using Sunset.Application.DTOs.Auth;
using Sunset.Application.DTOs.Users;
using Sunset.Application.Exceptions;
using Sunset.Application.Interfaces;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Domain.Entities;
using Sunset.Domain.Enums;

namespace Sunset.Application.Services;

public class AuthService(
    IUserRepository userRepository,
    IRefreshTokenRepository refreshTokenRepository,
    IPasswordResetTokenRepository passwordResetTokenRepository,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    IGoogleIdTokenVerifier googleIdTokenVerifier,
    IEmailSender emailSender,
    IFrontendUrlProvider frontendUrlProvider,
    ILegalAcceptanceService legalAcceptanceService) : IAuthService
{
    private static readonly TimeSpan PasswordResetTokenLifetime = TimeSpan.FromMinutes(30);

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        if (await userRepository.ExistsByEmailAsync(request.Email, cancellationToken))
            throw new ConflictException("Email is already registered.");

        // Before creating anything: a stale version means the client showed an outdated text.
        await legalAcceptanceService.EnsureCurrentAsync(request.TermsVersion, request.PrivacyVersion, cancellationToken);

        var passwordHash = passwordHasher.Hash(request.Password);
        var user = new User(request.Name, request.Email, passwordHash);

        await userRepository.AddAsync(user, cancellationToken);
        await legalAcceptanceService.RecordAsync(user.Id, request.TermsVersion, request.PrivacyVersion, LegalAcceptanceSource.Registration, cancellationToken);

        return await IssueTokensAsync(user, cancellationToken);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByEmailAsync(request.Email, cancellationToken);
        if (user is null || !passwordHasher.Verify(request.Password, user.PasswordHash))
            throw new UnauthorizedActionException("Invalid email or password.");

        // After the password check on purpose: telling a wrong-password caller that the account
        // is banned would confirm the account exists.
        EnsureNotBanned(user);

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
        await legalAcceptanceService.EnsureCurrentAsync(request.TermsVersion, request.PrivacyVersion, cancellationToken);

        var user = await userRepository.GetByEmailAsync(googleUser.Email, cancellationToken);
        var isNewAccount = user is null;
        if (user is null)
        {
            var unusablePasswordHash = passwordHasher.Hash(Guid.NewGuid().ToString());
            user = new User(googleUser.Name, googleUser.Email, unusablePasswordHash, hasPassword: false);
            await userRepository.AddAsync(user, cancellationToken);
        }

        EnsureNotBanned(user);

        // Accepting on the sign-up screen counts as registration; on an existing account it is a re-acceptance.
        await legalAcceptanceService.RecordAsync(user.Id, request.TermsVersion, request.PrivacyVersion,
            isNewAccount ? LegalAcceptanceSource.Registration : LegalAcceptanceSource.Reacceptance, cancellationToken);

        return await IssueTokensAsync(user, cancellationToken);
    }

    public async Task<AuthResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default)
    {
        var tokenHash = Hash(request.RefreshToken);
        var storedToken = await refreshTokenRepository.GetByTokenHashAsync(tokenHash, cancellationToken);
        if (storedToken is null || !storedToken.IsActive)
            throw new UnauthorizedActionException("Invalid or expired refresh token.");

        var user = storedToken.User;
        EnsureNotBanned(user);

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

    public async Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByEmailAsync(request.Email, cancellationToken);
        // Silently no-op for an unknown email - responding differently here would let a caller
        // enumerate which addresses have accounts by watching for a 404 vs. this endpoint's
        // otherwise-always-204.
        if (user is null)
            return;

        var rawToken = tokenService.GenerateOpaqueToken();
        var expiresAt = DateTime.UtcNow.Add(PasswordResetTokenLifetime);
        await passwordResetTokenRepository.AddAsync(new PasswordResetToken(user.Id, Hash(rawToken), expiresAt), cancellationToken);

        var resetLink = $"{frontendUrlProvider.BaseUrl.TrimEnd('/')}/reset-password?token={Uri.EscapeDataString(rawToken)}";
        var htmlBody = $"""
            <p>Olá, {user.Name}!</p>
            <p>Recebemos uma solicitação para redefinir sua senha no Sunsetsss. Clique no link abaixo para escolher uma nova senha:</p>
            <p><a href="{resetLink}">{resetLink}</a></p>
            <p>Esse link expira em 30 minutos. Se você não pediu essa redefinição, pode ignorar este e-mail.</p>
            """;

        await emailSender.SendAsync(user.Email, "Redefinição de senha - Sunsetsss", htmlBody, cancellationToken);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var tokenHash = Hash(request.Token);
        var storedToken = await passwordResetTokenRepository.GetByTokenHashAsync(tokenHash, cancellationToken);
        if (storedToken is null || !storedToken.IsActive)
            throw new UnauthorizedActionException("Invalid or expired reset token.");

        storedToken.User.ChangePassword(passwordHasher.Hash(request.NewPassword));
        storedToken.MarkUsed();
        await passwordResetTokenRepository.SaveChangesAsync(cancellationToken);

        // Forces re-login everywhere - whoever reset the password (hopefully the account owner,
        // but the whole point of a reset flow is that we can't be sure) shouldn't leave every
        // other already-logged-in session still valid, same reasoning as DeleteAccountAsync.
        await refreshTokenRepository.RevokeAllForUserAsync(storedToken.UserId, cancellationToken);
    }

    private static void EnsureNotBanned(User user)
    {
        if (!user.IsBanned(DateTime.UtcNow))
            return;

        var errors = new Dictionary<string, string[]> { ["reason"] = [user.BanReason ?? string.Empty] };
        if (user.BannedUntil is { } until)
            errors["bannedUntil"] = [until.ToString("O")];

        throw new ForbiddenException("This account is banned.", errors);
    }

    private async Task<AuthResponse> IssueTokensAsync(User user, CancellationToken cancellationToken)
    {
        var (accessToken, accessTokenExpiresAt) = tokenService.GenerateAccessToken(user);
        var (refreshToken, refreshTokenExpiresAt) = tokenService.GenerateRefreshToken();

        var refreshTokenEntity = new RefreshToken(user.Id, Hash(refreshToken), refreshTokenExpiresAt);
        await refreshTokenRepository.AddAsync(refreshTokenEntity, cancellationToken);

        var legalAcceptance = await legalAcceptanceService.GetStatusAsync(user.Id, cancellationToken);

        return new AuthResponse(accessToken, refreshToken, accessTokenExpiresAt, user.ToResponse(), legalAcceptance);
    }

    private static string Hash(string value) => TokenHasher.Hash(value);
}
