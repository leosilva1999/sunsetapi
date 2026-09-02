using Moq;
using Sunset.Application.Common;
using Sunset.Application.DTOs.Photos;
using Sunset.Application.Exceptions;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Application.Services;
using Sunset.Domain.Entities;
using Sunset.Domain.Enums;

namespace Sunset.UnitTests.Services;

public class PhotoServiceTests
{
    private readonly Mock<IPhotoRepository> _photoRepository = new();
    private readonly Mock<ILocationRepository> _locationRepository = new();
    private readonly PhotoService _sut;

    public PhotoServiceTests()
    {
        _sut = new PhotoService(_photoRepository.Object, _locationRepository.Object);
    }

    private static Photo CreatePhoto(User user, Location location, Guid? userId = null)
    {
        var photo = new Photo(userId ?? user.Id, location.Id, "https://sunset.com/photo.jpg", "Linda vista");
        typeof(Photo).GetProperty(nameof(Photo.User))!.SetValue(photo, user);
        typeof(Photo).GetProperty(nameof(Photo.Location))!.SetValue(photo, location);
        return photo;
    }

    [Fact]
    public async Task GetFeedAsync_WithAuthenticatedUser_MarksLikedPhotos()
    {
        var user = new User("Ana", "ana@sunset.com", "hashed");
        var location = new Location("Praia do Rosa", -28.13, -48.62, "Imbituba");
        var photo = CreatePhoto(user, location);
        var currentUserId = Guid.NewGuid();

        _photoRepository
            .Setup(r => r.GetFeedAsync(PhotoSortOption.Recent, null, 20, default))
            .ReturnsAsync(new CursorPagedResult<Photo>([photo], null, false));
        _photoRepository
            .Setup(r => r.GetLikedPhotoIdsAsync(currentUserId, It.Is<IEnumerable<Guid>>(ids => ids.Contains(photo.Id)), default))
            .ReturnsAsync(new HashSet<Guid> { photo.Id });

        var page = await _sut.GetFeedAsync(PhotoSortOption.Recent, null, 20, currentUserId);

        Assert.True(page.Items[0].LikedByCurrentUser);
    }

    [Fact]
    public async Task GetFeedAsync_WithAnonymousUser_DoesNotQueryLikes()
    {
        var user = new User("Ana", "ana@sunset.com", "hashed");
        var location = new Location("Praia do Rosa", -28.13, -48.62, "Imbituba");
        var photo = CreatePhoto(user, location);

        _photoRepository
            .Setup(r => r.GetFeedAsync(PhotoSortOption.Recent, null, 20, default))
            .ReturnsAsync(new CursorPagedResult<Photo>([photo], null, false));

        var page = await _sut.GetFeedAsync(PhotoSortOption.Recent, null, 20, null);

        Assert.False(page.Items[0].LikedByCurrentUser);
        _photoRepository.Verify(r => r.GetLikedPhotoIdsAsync(It.IsAny<Guid>(), It.IsAny<IEnumerable<Guid>>(), default), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_WithUnknownLocation_ThrowsNotFoundException()
    {
        _locationRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default)).ReturnsAsync((Location?)null);

        var request = new CreatePhotoRequest(Guid.NewGuid(), "https://sunset.com/photo.jpg", "Linda vista");

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.CreateAsync(Guid.NewGuid(), request));
        _photoRepository.Verify(r => r.AddAsync(It.IsAny<Photo>(), default), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_WithExistingLocation_CreatesAndReturnsPhoto()
    {
        var user = new User("Ana", "ana@sunset.com", "hashed");
        var location = new Location("Praia do Rosa", -28.13, -48.62, "Imbituba");
        _locationRepository.Setup(r => r.GetByIdAsync(location.Id, default)).ReturnsAsync(location);

        Photo? added = null;
        _photoRepository
            .Setup(r => r.AddAsync(It.IsAny<Photo>(), default))
            .Callback<Photo, CancellationToken>((p, _) => added = p)
            .Returns(Task.CompletedTask);
        _photoRepository
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default))
            .ReturnsAsync(() => added is null ? null : CreatePhoto(user, location, added.UserId));

        var request = new CreatePhotoRequest(location.Id, "https://sunset.com/photo.jpg", "Linda vista");
        var response = await _sut.CreateAsync(user.Id, request);

        Assert.Equal("Linda vista", response.Caption);
        Assert.Equal(location.Id, response.LocationId);
    }

    [Fact]
    public async Task DeleteAsync_WhenNotAuthor_ThrowsUnauthorizedActionException()
    {
        var user = new User("Ana", "ana@sunset.com", "hashed");
        var location = new Location("Praia do Rosa", -28.13, -48.62, "Imbituba");
        var photo = CreatePhoto(user, location);

        _photoRepository.Setup(r => r.GetByIdAsync(photo.Id, default)).ReturnsAsync(photo);

        await Assert.ThrowsAsync<UnauthorizedActionException>(() => _sut.DeleteAsync(Guid.NewGuid(), photo.Id));
        _photoRepository.Verify(r => r.RemoveAsync(It.IsAny<Photo>(), default), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_WhenAuthor_RemovesPhoto()
    {
        var user = new User("Ana", "ana@sunset.com", "hashed");
        var location = new Location("Praia do Rosa", -28.13, -48.62, "Imbituba");
        var photo = CreatePhoto(user, location);

        _photoRepository.Setup(r => r.GetByIdAsync(photo.Id, default)).ReturnsAsync(photo);

        await _sut.DeleteAsync(user.Id, photo.Id);

        _photoRepository.Verify(r => r.RemoveAsync(photo, default), Times.Once);
    }

    [Fact]
    public async Task LikeAsync_WhenAlreadyLiked_IsIdempotent()
    {
        var user = new User("Ana", "ana@sunset.com", "hashed");
        var location = new Location("Praia do Rosa", -28.13, -48.62, "Imbituba");
        var photo = CreatePhoto(user, location);
        var likerId = Guid.NewGuid();

        _photoRepository.Setup(r => r.GetByIdAsync(photo.Id, default)).ReturnsAsync(photo);
        _photoRepository.Setup(r => r.GetLikeAsync(likerId, photo.Id, default)).ReturnsAsync(new Like(likerId, photo.Id));

        await _sut.LikeAsync(likerId, photo.Id);

        _photoRepository.Verify(r => r.AddLikeAsync(It.IsAny<Like>(), default), Times.Never);
        Assert.Equal(0, photo.LikesCount);
    }

    [Fact]
    public async Task LikeAsync_WhenNotLiked_IncrementsAndAddsLike()
    {
        var user = new User("Ana", "ana@sunset.com", "hashed");
        var location = new Location("Praia do Rosa", -28.13, -48.62, "Imbituba");
        var photo = CreatePhoto(user, location);
        var likerId = Guid.NewGuid();

        _photoRepository.Setup(r => r.GetByIdAsync(photo.Id, default)).ReturnsAsync(photo);
        _photoRepository.Setup(r => r.GetLikeAsync(likerId, photo.Id, default)).ReturnsAsync((Like?)null);

        await _sut.LikeAsync(likerId, photo.Id);

        Assert.Equal(1, photo.LikesCount);
        _photoRepository.Verify(r => r.AddLikeAsync(It.Is<Like>(l => l.UserId == likerId && l.PhotoId == photo.Id), default), Times.Once);
    }

    [Fact]
    public async Task AddCommentAsync_WithUnknownPhoto_ThrowsNotFoundException()
    {
        _photoRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default)).ReturnsAsync((Photo?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.AddCommentAsync(Guid.NewGuid(), Guid.NewGuid(), new CreateCommentRequest("Muito lindo!")));
    }

    [Fact]
    public async Task DeleteCommentAsync_WhenNotAuthor_ThrowsUnauthorizedActionException()
    {
        var user = new User("Ana", "ana@sunset.com", "hashed");
        var comment = new Comment(user.Id, Guid.NewGuid(), "Muito lindo!");
        typeof(Comment).GetProperty(nameof(Comment.User))!.SetValue(comment, user);

        _photoRepository.Setup(r => r.GetCommentByIdAsync(comment.Id, default)).ReturnsAsync(comment);

        await Assert.ThrowsAsync<UnauthorizedActionException>(() => _sut.DeleteCommentAsync(Guid.NewGuid(), comment.Id));
        _photoRepository.Verify(r => r.RemoveCommentAsync(It.IsAny<Comment>(), default), Times.Never);
    }
}
