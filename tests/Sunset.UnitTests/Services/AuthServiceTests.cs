using Moq;
using Sunset.Application.DTOs.Auth;
using Sunset.Application.Exceptions;
using Sunset.Application.Interfaces;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Application.Services;
using Sunset.Domain.Entities;

namespace Sunset.UnitTests.Services;

public class AuthServiceTests
{
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<IRefreshTokenRepository> _refreshTokenRepository = new();
    private readonly Mock<IPasswordHasher> _passwordHasher = new();
    private readonly Mock<ITokenService> _tokenService = new();
    private readonly AuthService _sut;

    public AuthServiceTests()
    {
        _sut = new AuthService(
            _userRepository.Object,
            _refreshTokenRepository.Object,
            _passwordHasher.Object,
            _tokenService.Object);

        _tokenService
            .Setup(t => t.GenerateAccessToken(It.IsAny<User>()))
            .Returns(("access-token", DateTime.UtcNow.AddMinutes(15)));

        _tokenService
            .Setup(t => t.GenerateRefreshToken())
            .Returns(("refresh-token", DateTime.UtcNow.AddDays(30)));
    }

    [Fact]
    public async Task RegisterAsync_WithNewEmail_CreatesUserAndReturnsTokens()
    {
        _userRepository.Setup(r => r.ExistsByEmailAsync("new@sunset.com", default)).ReturnsAsync(false);
        _passwordHasher.Setup(h => h.Hash("password123")).Returns("hashed");

        var request = new RegisterRequest("Ana", "new@sunset.com", "password123");

        var response = await _sut.RegisterAsync(request);

        Assert.Equal("access-token", response.AccessToken);
        Assert.Equal("refresh-token", response.RefreshToken);
        Assert.Equal("new@sunset.com", response.User.Email);
        _userRepository.Verify(r => r.AddAsync(It.Is<User>(u => u.Email == "new@sunset.com"), default), Times.Once);
        _refreshTokenRepository.Verify(r => r.AddAsync(It.IsAny<RefreshToken>(), default), Times.Once);
    }

    [Fact]
    public async Task RegisterAsync_WithExistingEmail_ThrowsConflictException()
    {
        _userRepository.Setup(r => r.ExistsByEmailAsync("taken@sunset.com", default)).ReturnsAsync(true);

        var request = new RegisterRequest("Ana", "taken@sunset.com", "password123");

        await Assert.ThrowsAsync<ConflictException>(() => _sut.RegisterAsync(request));
        _userRepository.Verify(r => r.AddAsync(It.IsAny<User>(), default), Times.Never);
    }

    [Fact]
    public async Task LoginAsync_WithWrongPassword_ThrowsUnauthorizedActionException()
    {
        var user = new User("Ana", "ana@sunset.com", "hashed");
        _userRepository.Setup(r => r.GetByEmailAsync("ana@sunset.com", default)).ReturnsAsync(user);
        _passwordHasher.Setup(h => h.Verify("wrong-password", "hashed")).Returns(false);

        var request = new LoginRequest("ana@sunset.com", "wrong-password");

        await Assert.ThrowsAsync<UnauthorizedActionException>(() => _sut.LoginAsync(request));
    }

    [Fact]
    public async Task LoginAsync_WithUnknownEmail_ThrowsUnauthorizedActionException()
    {
        _userRepository.Setup(r => r.GetByEmailAsync("ghost@sunset.com", default)).ReturnsAsync((User?)null);

        var request = new LoginRequest("ghost@sunset.com", "password123");

        await Assert.ThrowsAsync<UnauthorizedActionException>(() => _sut.LoginAsync(request));
    }

    [Fact]
    public async Task LoginAsync_WithCorrectCredentials_ReturnsTokens()
    {
        var user = new User("Ana", "ana@sunset.com", "hashed");
        _userRepository.Setup(r => r.GetByEmailAsync("ana@sunset.com", default)).ReturnsAsync(user);
        _passwordHasher.Setup(h => h.Verify("password123", "hashed")).Returns(true);

        var request = new LoginRequest("ana@sunset.com", "password123");

        var response = await _sut.LoginAsync(request);

        Assert.Equal("access-token", response.AccessToken);
    }

    [Fact]
    public async Task RefreshAsync_WithUnknownToken_ThrowsUnauthorizedActionException()
    {
        _refreshTokenRepository
            .Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), default))
            .ReturnsAsync((RefreshToken?)null);

        var request = new RefreshTokenRequest("some-token");

        await Assert.ThrowsAsync<UnauthorizedActionException>(() => _sut.RefreshAsync(request));
    }

    [Fact]
    public async Task RefreshAsync_WithRevokedToken_ThrowsUnauthorizedActionException()
    {
        var user = new User("Ana", "ana@sunset.com", "hashed");
        var storedToken = new RefreshToken(user.Id, "some-hash", DateTime.UtcNow.AddDays(1));
        storedToken.Revoke();
        SetUserOnToken(storedToken, user);

        _refreshTokenRepository
            .Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), default))
            .ReturnsAsync(storedToken);

        var request = new RefreshTokenRequest("some-token");

        await Assert.ThrowsAsync<UnauthorizedActionException>(() => _sut.RefreshAsync(request));
    }

    [Fact]
    public async Task RefreshAsync_WithActiveToken_RotatesAndRevokesOldToken()
    {
        var user = new User("Ana", "ana@sunset.com", "hashed");
        var storedToken = new RefreshToken(user.Id, "some-hash", DateTime.UtcNow.AddDays(1));
        SetUserOnToken(storedToken, user);

        _refreshTokenRepository
            .Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), default))
            .ReturnsAsync(storedToken);

        var request = new RefreshTokenRequest("some-token");

        var response = await _sut.RefreshAsync(request);

        Assert.Equal("refresh-token", response.RefreshToken);
        Assert.False(storedToken.IsActive);
        _refreshTokenRepository.Verify(r => r.AddAsync(It.IsAny<RefreshToken>(), default), Times.Once);
        _refreshTokenRepository.Verify(r => r.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task LogoutAsync_WithActiveToken_RevokesIt()
    {
        var user = new User("Ana", "ana@sunset.com", "hashed");
        var storedToken = new RefreshToken(user.Id, "some-hash", DateTime.UtcNow.AddDays(1));
        SetUserOnToken(storedToken, user);

        _refreshTokenRepository
            .Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), default))
            .ReturnsAsync(storedToken);

        await _sut.LogoutAsync(new RefreshTokenRequest("some-token"));

        Assert.False(storedToken.IsActive);
        _refreshTokenRepository.Verify(r => r.SaveChangesAsync(default), Times.Once);
    }

    private static void SetUserOnToken(RefreshToken token, User user)
    {
        typeof(RefreshToken).GetProperty(nameof(RefreshToken.User))!.SetValue(token, user);
    }
}
