using Sunset.Domain.Entities;

namespace Sunset.Application.Interfaces;

public interface ITokenService
{
    (string Token, DateTime ExpiresAt) GenerateAccessToken(User user);
    (string Token, DateTime ExpiresAt) GenerateRefreshToken();
}
