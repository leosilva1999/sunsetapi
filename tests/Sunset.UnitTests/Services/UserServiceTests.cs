using Moq;
using Sunset.Application.Common;
using Sunset.Application.DTOs.Users;
using Sunset.Application.Exceptions;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Application.Services;
using Sunset.Domain.Entities;

namespace Sunset.UnitTests.Services;

public class UserServiceTests
{
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<IPhotoRepository> _photoRepository = new();
    private readonly UserService _sut;

    public UserServiceTests()
    {
        _sut = new UserService(_userRepository.Object, _photoRepository.Object);
    }

    [Fact]
    public async Task GetByIdAsync_WithExistingUser_ReturnsResponse()
    {
        var user = new User("Ana", "ana@sunset.com", "hashed");
        _userRepository.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);

        var response = await _sut.GetByIdAsync(user.Id);

        Assert.Equal(user.Id, response.Id);
        Assert.Equal("Ana", response.Name);
    }

    [Fact]
    public async Task GetByIdAsync_WithUnknownUser_ThrowsNotFoundException()
    {
        _userRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default)).ReturnsAsync((User?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task UpdateProfileAsync_WithAllFieldsSet_UpdatesAndReturnsResponse()
    {
        var user = new User("Ana", "ana@sunset.com", "hashed");
        _userRepository.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);

        var request = new UpdateProfileRequest(
            Optional<string>.Of("Ana Souza"),
            Optional<string?>.Of("https://sunset.com/avatar.png"),
            Optional<string?>.Of("Adoro um pôr do sol."));

        var response = await _sut.UpdateProfileAsync(user.Id, request);

        Assert.Equal("Ana Souza", response.Name);
        Assert.Equal("https://sunset.com/avatar.png", response.AvatarUrl);
        Assert.Equal("Adoro um pôr do sol.", response.Bio);
        _userRepository.Verify(r => r.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task UpdateProfileAsync_WithOnlyBioSet_LeavesNameAndAvatarUrlUnchanged()
    {
        var user = new User("Ana", "ana@sunset.com", "hashed", "https://sunset.com/original.png");
        _userRepository.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);

        var request = new UpdateProfileRequest(Optional<string>.Unset, Optional<string?>.Unset, Optional<string?>.Of("Nova bio."));

        var response = await _sut.UpdateProfileAsync(user.Id, request);

        Assert.Equal("Ana", response.Name);
        Assert.Equal("https://sunset.com/original.png", response.AvatarUrl);
        Assert.Equal("Nova bio.", response.Bio);
    }

    [Fact]
    public async Task UpdateProfileAsync_WithBioExplicitlyNull_ClearsBio()
    {
        var user = new User("Ana", "ana@sunset.com", "hashed");
        user.UpdateProfile("Ana", null, "Bio antiga.");
        _userRepository.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);

        var request = new UpdateProfileRequest(Optional<string>.Unset, Optional<string?>.Unset, Optional<string?>.Of(null));

        var response = await _sut.UpdateProfileAsync(user.Id, request);

        Assert.Null(response.Bio);
    }

    [Fact]
    public async Task UpdateProfileAsync_WithUnknownUser_ThrowsNotFoundException()
    {
        _userRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default)).ReturnsAsync((User?)null);

        var request = new UpdateProfileRequest(Optional<string>.Of("Ana Souza"), Optional<string?>.Unset, Optional<string?>.Unset);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.UpdateProfileAsync(Guid.NewGuid(), request));
        _userRepository.Verify(r => r.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task GetPhotosAsync_WithUnknownUser_ThrowsNotFoundException()
    {
        _userRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default)).ReturnsAsync((User?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetPhotosAsync(Guid.NewGuid(), null, 20));
        _photoRepository.Verify(r => r.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<int>(), default), Times.Never);
    }

    [Fact]
    public async Task GetPhotosAsync_WithExistingUser_ReturnsMappedPage()
    {
        var user = new User("Ana", "ana@sunset.com", "hashed");
        var location = new Location("Praia do Rosa", -28.13, -48.62, "Imbituba");
        var photo = new Photo(user.Id, Guid.NewGuid(), "https://sunset.com/photo.jpg", "Linda vista");
        SetNavigation(photo, user, location);

        _userRepository.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);
        _photoRepository
            .Setup(r => r.GetByUserIdAsync(user.Id, null, 20, default))
            .ReturnsAsync(new CursorPagedResult<Photo>([photo], null, false));

        var page = await _sut.GetPhotosAsync(user.Id, null, 20);

        Assert.Single(page.Items);
        Assert.Equal(photo.Id, page.Items[0].Id);
        Assert.False(page.HasMore);
    }

    private static void SetNavigation(Photo photo, User user, Location location)
    {
        typeof(Photo).GetProperty(nameof(Photo.User))!.SetValue(photo, user);
        typeof(Photo).GetProperty(nameof(Photo.Location))!.SetValue(photo, location);
    }
}
