using Sunset.Application.DTOs.Auth;

namespace Sunset.Application.Interfaces;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task<AuthResponse> GoogleLoginAsync(GoogleAuthRequest request, CancellationToken cancellationToken = default);
    Task<AuthResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default);
    Task LogoutAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default);
    Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default);
    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default);
}
