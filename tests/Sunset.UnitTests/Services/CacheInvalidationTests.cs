using Moq;
using Sunset.Application.Common;
using Sunset.Application.Cities;
using Sunset.Application.DTOs.Cities;
using Sunset.Application.DTOs.Locations;
using Sunset.Application.DTOs.Moderation;
using Sunset.Application.DTOs.Photos;
using Sunset.Application.Interfaces;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Application.Services;
using Sunset.Domain.Entities;
using Sunset.Domain.Enums;

namespace Sunset.UnitTests.Services;

// Each write that changes what a cached public response shows must drop the right cache groups,
// AFTER the change is saved; a request that fails must not touch the cache.

public class LocationCacheInvalidationTests
{
    private const ResponseCacheTag LocationsAndCities = ResponseCacheTag.Locations | ResponseCacheTag.Cities;

    private readonly Mock<ILocationRepository> _locations = new();
    private readonly Mock<ICityRepository> _cities = new();
    private readonly Mock<IResponseCacheInvalidator> _cache = new();
    private readonly LocationService _sut;

    public LocationCacheInvalidationTests()
    {
        _cities.Setup(r => r.FindMunicipalitiesAsync(It.IsAny<string>(), It.IsAny<string?>(), default)).ReturnsAsync([]);
        _cities.Setup(r => r.FindLocalitiesAsync(It.IsAny<string>(), It.IsAny<string?>(), default)).ReturnsAsync([]);
        _locations.Setup(r => r.GetStatsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), default)).ReturnsAsync(new Dictionary<Guid, LocationStats>());
        _sut = new LocationService(_locations.Object, new Mock<IPhotoRepository>().Object, new Mock<ISunsetTimeService>().Object,
            _cities.Object, new CityResolver(_cities.Object), _cache.Object);
    }

    [Fact]
    public async Task CreatingALocation_DropsTheLocationAndCityResponses_AfterSaving()
    {
        var order = new List<string>();
        _locations.Setup(r => r.AddAsync(It.IsAny<Location>(), default)).Callback(() => order.Add("save")).Returns(Task.CompletedTask);
        _cache.Setup(c => c.InvalidateAsync(LocationsAndCities, default)).Callback(() => order.Add("evict")).Returns(Task.CompletedTask);

        await _sut.CreateAsync(Guid.NewGuid(), new CreateLocationRequest("Praia", -28, -48, "Imbituba"));

        Assert.Equal(["save", "evict"], order);
    }

    [Fact]
    public async Task ARejectedCreate_DoesNotTouchTheCache()
    {
        _cities.Setup(r => r.GetActiveByIdAsync(It.IsAny<Guid>(), default)).ReturnsAsync((City?)null);

        await Assert.ThrowsAnyAsync<Exception>(() => _sut.CreateAsync(Guid.NewGuid(), new CreateLocationRequest("X", 0, 0, CityId: Guid.NewGuid())));

        _cache.Verify(c => c.InvalidateAsync(It.IsAny<ResponseCacheTag>(), default), Times.Never);
    }

    [Fact]
    public async Task Rating_AndRemovingARating_DropTheLocationAndCityResponses()
    {
        var location = new Location("Praia do Rosa", -28.13, -48.62, "Imbituba");
        var user = new User("Ana", "ana@sunset.com", "hashed");
        var rating = new Rating(user.Id, location.Id, 5);
        typeof(Rating).GetProperty(nameof(Rating.User))!.SetValue(rating, user);
        _locations.Setup(r => r.GetByIdAsync(location.Id, default)).ReturnsAsync(location);
        _locations.SetupSequence(r => r.GetRatingAsync(user.Id, location.Id, default)).ReturnsAsync((Rating?)null).ReturnsAsync(rating);

        await _sut.RateAsync(user.Id, location.Id, new CreateRatingRequest(5));
        _cache.Verify(c => c.InvalidateAsync(LocationsAndCities, default), Times.Once);

        _locations.Setup(r => r.GetRatingAsync(user.Id, location.Id, default)).ReturnsAsync(rating);
        await _sut.DeleteRatingAsync(user.Id, location.Id);
        _cache.Verify(c => c.InvalidateAsync(LocationsAndCities, default), Times.Exactly(2));
    }
}

public class PhotoCacheInvalidationTests
{
    private readonly Mock<IPhotoRepository> _photos = new();
    private readonly Mock<ILocationRepository> _locations = new();
    private readonly Mock<IUploadedImageSanitizer> _sanitizer = new();
    private readonly Mock<IResponseCacheInvalidator> _cache = new();
    private readonly PhotoService _sut;

    public PhotoCacheInvalidationTests()
    {
        _sanitizer.Setup(s => s.SanitizeAndMeasureAsync(It.IsAny<string>(), default)).ReturnsAsync(new ImageDimensions(1200, 800));
        _sut = new PhotoService(_photos.Object, _locations.Object, new Mock<IModerationActionRepository>().Object,
            new Mock<INotificationRepository>().Object, _sanitizer.Object, _cache.Object);
    }

    private static Photo PhotoOf(User user, Location location)
    {
        var photo = new Photo(user.Id, location.Id, "https://x/p.jpg", null);
        typeof(Photo).GetProperty(nameof(Photo.User))!.SetValue(photo, user);
        typeof(Photo).GetProperty(nameof(Photo.Location))!.SetValue(photo, location);
        return photo;
    }

    [Fact]
    public async Task APublishedPhoto_DropsEveryGroup_AfterSaving()
    {
        var user = new User("Ana", "ana@sunset.com", "hashed");
        var location = new Location("Praia do Rosa", -28.13, -48.62, "Imbituba");
        _locations.Setup(r => r.GetByIdAsync(location.Id, default)).ReturnsAsync(location);
        var order = new List<string>();
        _photos.Setup(r => r.AddAsync(It.IsAny<Photo>(), default)).Callback(() => order.Add("save")).Returns(Task.CompletedTask);
        _photos.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default)).ReturnsAsync(PhotoOf(user, location));
        _cache.Setup(c => c.InvalidateAsync(ResponseCacheTag.All, default)).Callback(() => order.Add("evict")).Returns(Task.CompletedTask);

        await _sut.CreateAsync(user.Id, new CreatePhotoRequest(location.Id, "https://x/p.jpg", null));

        Assert.Equal(["save", "evict"], order);
    }

    [Fact]
    public async Task ARejectedImage_DoesNotTouchTheCache()
    {
        var location = new Location("Praia do Rosa", -28.13, -48.62, "Imbituba");
        _locations.Setup(r => r.GetByIdAsync(location.Id, default)).ReturnsAsync(location);
        _sanitizer.Setup(s => s.SanitizeAndMeasureAsync(It.IsAny<string>(), default)).ReturnsAsync((ImageDimensions?)null);

        await Assert.ThrowsAnyAsync<Exception>(() => _sut.CreateAsync(Guid.NewGuid(), new CreatePhotoRequest(location.Id, "https://x/bad.jpg", null)));

        _cache.Verify(c => c.InvalidateAsync(It.IsAny<ResponseCacheTag>(), default), Times.Never);
    }

    [Fact]
    public async Task ADeletedPhoto_DropsEveryGroup_ByTheAuthorOrAModerator()
    {
        var user = new User("Ana", "ana@sunset.com", "hashed");
        var location = new Location("Praia do Rosa", -28.13, -48.62, "Imbituba");
        var photo = PhotoOf(user, location);
        _photos.Setup(r => r.GetByIdAsync(photo.Id, default)).ReturnsAsync(photo);

        await _sut.DeleteAsync(user.Id, photo.Id, isModerator: false);

        _cache.Verify(c => c.InvalidateAsync(ResponseCacheTag.All, default), Times.Once);
    }

    [Fact]
    public async Task ADeleteThatIsNotAllowed_DoesNotTouchTheCache()
    {
        var user = new User("Ana", "ana@sunset.com", "hashed");
        var location = new Location("Praia do Rosa", -28.13, -48.62, "Imbituba");
        var photo = PhotoOf(user, location);
        _photos.Setup(r => r.GetByIdAsync(photo.Id, default)).ReturnsAsync(photo);

        await Assert.ThrowsAnyAsync<Exception>(() => _sut.DeleteAsync(Guid.NewGuid(), photo.Id, isModerator: false));

        _cache.Verify(c => c.InvalidateAsync(It.IsAny<ResponseCacheTag>(), default), Times.Never);
    }
}

public class ModerationCacheInvalidationTests
{
    private readonly Mock<ILocationRepository> _locations = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IPhotoRepository> _photos = new();
    private readonly Mock<ICityRepository> _cities = new();
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<IResponseCacheInvalidator> _cache = new();
    private readonly ModerationService _sut;

    public ModerationCacheInvalidationTests()
    {
        _locations.Setup(r => r.GetStatsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), default)).ReturnsAsync(new Dictionary<Guid, LocationStats>());
        _hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("unusable");
        _sut = new ModerationService(
            new Mock<IReportRepository>().Object, new Mock<IModerationActionRepository>().Object, new Mock<ILegalDocumentRepository>().Object,
            _users.Object, _photos.Object, new Mock<IRefreshTokenRepository>().Object, new Mock<IEmailSender>().Object,
            new Mock<IBanStatusService>().Object, _locations.Object, _hasher.Object, _cities.Object, new CityResolver(_cities.Object), _cache.Object);
    }

    private Location Visible()
    {
        var location = new Location("Praia do Rosa", -28.13, -48.62, "Imbituba");
        _locations.Setup(r => r.GetByIdAsync(location.Id, default)).ReturnsAsync(location);
        return location;
    }

    [Fact]
    public async Task DeletingRestoringAndMergingLocations_DropEveryGroup()
    {
        var moderator = Guid.NewGuid();
        var deleted = Visible();
        await _sut.DeleteLocationAsync(moderator, deleted.Id);

        _locations.Setup(r => r.GetByIdIncludingDeletedAsync(deleted.Id, default)).ReturnsAsync(deleted);
        await _sut.RestoreLocationAsync(moderator, deleted.Id);

        var source = Visible();
        var target = Visible();
        await _sut.MergeLocationsAsync(moderator, source.Id, new MergeLocationsRequest(target.Id));

        _cache.Verify(c => c.InvalidateAsync(ResponseCacheTag.All, default), Times.Exactly(3));
    }

    [Fact]
    public async Task FixingTheCoordinates_AndChangingTheCity_DropLocationsAndCities()
    {
        var moderator = Guid.NewGuid();
        var location = Visible();
        await _sut.UpdateLocationCoordinatesAsync(moderator, location.Id, new UpdateLocationCoordinatesRequest(-28.2, -48.7));

        var city = new City(CityKind.Municipality, "Imbituba", "Imbituba", null, "imbituba", "BR", "SC", "imbituba-sc", "geonames", 1, "4207304", null, -28.2, -48.7, "America/Sao_Paulo", 1);
        _cities.Setup(r => r.GetActiveByIdAsync(city.Id, default)).ReturnsAsync(city);
        await _sut.ChangeLocationCityAsync(moderator, location.Id, new ChangeLocationCityRequest(city.Id));

        _cache.Verify(c => c.InvalidateAsync(ResponseCacheTag.Locations | ResponseCacheTag.Cities, default), Times.Exactly(2));
        _cache.Verify(c => c.InvalidateAsync(ResponseCacheTag.All, default), Times.Never);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public async Task RemovingAUser_DropsTheCaches_OnlyWhenTheirContentIsHidden(bool hideContent, int evictions)
    {
        var admin = new User("Admin", "admin@sunset.com", "hashed");
        admin.ChangeRole(UserRole.Admin);
        var target = new User("Alvo", "alvo@sunset.com", "hashed");
        _users.Setup(r => r.GetByIdAsync(admin.Id, default)).ReturnsAsync(admin);
        _users.Setup(r => r.GetByIdAsync(target.Id, default)).ReturnsAsync(target);
        _photos.Setup(r => r.GetCommentsByUserAsync(target.Id, default)).ReturnsAsync([]);

        await _sut.AnonymizeUserAsync(admin.Id, target.Id, new AnonymizeUserRequest("Spam", hideContent));

        _cache.Verify(c => c.InvalidateAsync(ResponseCacheTag.All, default), Times.Exactly(evictions));
    }
}
