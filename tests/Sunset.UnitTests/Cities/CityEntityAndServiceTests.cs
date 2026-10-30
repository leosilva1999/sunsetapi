using Moq;
using Sunset.Application.Common;
using Sunset.Application.DTOs.Cities;
using Sunset.Application.DTOs.Locations;
using Sunset.Application.DTOs.Photos;
using Sunset.Application.Exceptions;
using Sunset.Application.Interfaces;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Application.Services;
using Sunset.Application.Validators.Cities;
using Sunset.Domain.Entities;
using Sunset.Domain.Enums;

namespace Sunset.UnitTests.Cities;

public class CityEntityTests
{
    private static City Municipality(string slug = "imbituba-sc") =>
        new(CityKind.Municipality, "Imbituba", "Imbituba", null, "imbituba", "BR", "SC", slug, "geonames", 1, "4207304", null, -28.2, -48.6, "America/Sao_Paulo", 100);

    [Fact]
    public void UpdateFromSource_ChangesTheData_ButNeverTheSlug()
    {
        var city = Municipality();

        city.UpdateFromSource("Imbituba Nova", "Imbituba", "Alt", "imbituba nova alt", "SC", null, -28.3, -48.7, "America/Sao_Paulo", 999);

        Assert.Equal("Imbituba Nova", city.Name);
        Assert.Equal(999, city.Population);
        Assert.Equal("imbituba-sc", city.Slug);
    }

    [Fact]
    public void ALocality_NeedsAParent_AndAMunicipalityCannotHaveOne()
    {
        Assert.Throws<ArgumentException>(() =>
            new City(CityKind.Locality, "Praia", "Praia", null, "praia", "BR", "SC", "praia-x", "geonames", 2, null, null, 0, 0, null, 0));
        Assert.Throws<ArgumentException>(() =>
            new City(CityKind.Municipality, "X", "X", null, "x", "BR", "SC", "x-sc", "geonames", 3, null, Guid.NewGuid(), 0, 0, null, 0));
    }

    [Fact]
    public void Coordinates_AreValidated() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new City(CityKind.Municipality, "X", "X", null, "x", "BR", "SC", "x-sc", "geonames", 3, null, null, 91, 0, null, 0));

    [Fact]
    public void AdoptSource_KeepsTheRowAndItsSlug()
    {
        var manual = new City(CityKind.Municipality, "Boa", "Boa", null, "boa", "BR", "MT", "boa-mt", "manual", null, "5101837", null, 0, 0, null, 0);

        manual.AdoptSource("geonames", 42);

        Assert.Equal("geonames", manual.Source);
        Assert.Equal(42, manual.ExternalId);
        Assert.Equal("boa-mt", manual.Slug);
    }

    [Fact]
    public void Deactivate_AndActivate_Toggle()
    {
        var city = Municipality();
        city.Deactivate();
        Assert.False(city.IsActive);
        city.Activate();
        Assert.True(city.IsActive);
    }
}

public class CityListQueryValidatorTests
{
    private readonly CityListQueryValidator _sut = new();

    private static CityListQuery Query(string? q = null, string? country = null, string? state = null) =>
        new(q, CityKindFilter.Municipality, country, state, CitySortOption.Population, null, 20);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sa")]
    [InlineData("  sao  ")]
    public void ValidSearchTerms(string? q) => Assert.True(_sut.Validate(Query(q)).IsValid);

    [Theory]
    [InlineData("a")]
    [InlineData(" a ")]
    public void ASingleCharacterIsTooShort(string q)
    {
        var result = _sut.Validate(Query(q));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Q");
    }

    [Theory]
    [InlineData("BR", true)]
    [InlineData("br", true)]
    [InlineData("BRA", false)]
    [InlineData("1A", false)]
    public void CountryMustBeTwoLetters(string country, bool valid) =>
        Assert.Equal(valid, _sut.Validate(Query(country: country)).IsValid);

    [Fact]
    public void StateIsLimitedInLength() =>
        Assert.False(_sut.Validate(Query(state: new string('x', 11))).IsValid);

    [Theory]
    [InlineData(CitySortOption.Locations, true)]
    [InlineData(CitySortOption.Rating, true)]
    [InlineData(CitySortOption.Population, false)]
    [InlineData(CitySortOption.Name, false)]
    public void ActivitySorts_AreTheOnesThatNeedLocations(CitySortOption sort, bool byActivity) =>
        Assert.Equal(byActivity, new CityListQuery(null, CityKindFilter.Any, null, null, sort, null).SortsByActivity);
}

public class CityServiceTests
{
    private readonly Mock<ICityRepository> _cities = new();
    private readonly Mock<ILocationRepository> _locations = new();
    private readonly Mock<IPhotoService> _photos = new();
    private readonly Mock<ISunsetTimeService> _sunset = new();
    private readonly CityService _sut;

    public CityServiceTests()
    {
        _locations.Setup(r => r.GetStatsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), default))
            .ReturnsAsync(new Dictionary<Guid, LocationStats>());
        _cities.Setup(r => r.GetDetailExtrasAsync(It.IsAny<Guid>(), default))
            .ReturnsAsync(new CityDetailExtras(null, []));
        _sut = new CityService(_cities.Object, _locations.Object, _photos.Object, _sunset.Object);
    }

    private static City Municipality() =>
        new(CityKind.Municipality, "Imbituba", "Imbituba", null, "imbituba", "BR", "SC", "imbituba-sc", "geonames", 1, "4207304", null, -28.2, -48.6, "America/Sao_Paulo", 45286);

    private City WithLocations(int locations = 2, int photos = 5, int ratings = 3, decimal? avg = 4.5m)
    {
        var city = Municipality();
        _cities.Setup(r => r.GetBySlugAsync("imbituba-sc", default)).ReturnsAsync(city);
        _cities.Setup(r => r.GetStatsAsync(city.Id, default)).ReturnsAsync(new CityStats(locations, photos, ratings, avg, "https://img/c.jpg"));
        return city;
    }

    [Fact]
    public async Task GetBySlug_ReturnsTheCityPageInOneCall()
    {
        var city = WithLocations();
        _cities.Setup(r => r.GetDetailExtrasAsync(city.Id, default)).ReturnsAsync(new CityDetailExtras(
            new CityTopLocationResponse(Guid.NewGuid(), "Praia do Rosa", 4.9m),
            [new CityLocalityResponse("Praia do Rosa", "praia-do-rosa-imbituba-sc", 2)]));

        var response = await _sut.GetBySlugAsync("imbituba-sc");

        Assert.Equal("Imbituba", response.Name);
        Assert.Equal("SC", response.State);
        Assert.Equal("America/Sao_Paulo", response.TimeZoneId);
        Assert.Equal((2, 5, 3, 4.5m), (response.LocationsCount, response.PhotosCount, response.RatingsCount, response.AvgRating));
        Assert.Equal("https://img/c.jpg", response.CoverImageUrl);
        Assert.Equal("Praia do Rosa", response.TopLocation!.Name);
        Assert.Equal("praia-do-rosa-imbituba-sc", Assert.Single(response.Localities).Slug);
    }

    [Fact]
    public async Task GetBySlug_UnknownSlug_IsNotFound()
    {
        _cities.Setup(r => r.GetBySlugAsync(It.IsAny<string>(), default)).ReturnsAsync((City?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetBySlugAsync("nada-xx"));
    }

    [Fact]
    public async Task AMunicipalityWithoutLocations_HasNoPage_OnAnyOfTheCityEndpoints()
    {
        WithLocations(locations: 0, photos: 0, ratings: 0, avg: null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetBySlugAsync("imbituba-sc"));
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetLocationsAsync("imbituba-sc", CityLocationSort.Rating, null, 20));
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetPhotosAsync("imbituba-sc", PhotoSortOption.Top, null, 20, null));
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetSunsetTimeAsync("imbituba-sc", null));
    }

    [Fact]
    public async Task GetBySlug_ALocality_HasNoPublicPageYet()
    {
        var parent = Municipality();
        var locality = new City(CityKind.Locality, "Praia do Rosa", "Praia do Rosa", null, "praia do rosa", "BR", "SC", "praia-do-rosa-imbituba-sc", "geonames", 2, null, parent.Id, 0, 0, null, 0);
        _cities.Setup(r => r.GetBySlugAsync("praia-do-rosa-imbituba-sc", default)).ReturnsAsync(locality);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetBySlugAsync("praia-do-rosa-imbituba-sc"));
    }

    [Fact]
    public async Task GetLocations_MapsThePageWithCountsAndCity()
    {
        var city = WithLocations();
        var rosa = new Location("Praia do Rosa", -28.13, -48.65, "x");
        rosa.ChangeCity(city);
        _locations.Setup(r => r.GetByCityAsync(city.Id, CityLocationSort.Recent, "cur", 5, default))
            .ReturnsAsync(new CursorPagedResult<Location>([rosa], "next", true));
        _locations.Setup(r => r.GetStatsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), default))
            .ReturnsAsync(new Dictionary<Guid, LocationStats> { [rosa.Id] = new(2, 4, "https://img/p.jpg") });

        var page = await _sut.GetLocationsAsync("imbituba-sc", CityLocationSort.Recent, "cur", 5);

        var item = Assert.Single(page.Items);
        Assert.Equal("imbituba-sc", item.CitySlug);
        Assert.Equal(4, item.PhotosCount);
        Assert.Equal("next", page.NextCursor);
        Assert.True(page.HasMore);
    }

    [Fact]
    public async Task GetPhotos_DelegatesToThePhotoFeed_ForTheCurrentUser()
    {
        var city = WithLocations();
        var user = Guid.NewGuid();
        var expected = new CursorPagedResult<PhotoResponse>([], null, false);
        _photos.Setup(s => s.GetCityFeedAsync(city.Id, PhotoSortOption.Top, null, 20, user, default)).ReturnsAsync(expected);

        Assert.Same(expected, await _sut.GetPhotosAsync("imbituba-sc", PhotoSortOption.Top, null, 20, user));
    }

    [Fact]
    public async Task GetSunsetTime_UsesTheCityCentre()
    {
        WithLocations();
        var expected = new SunsetTimeResponse(new DateOnly(2026, 10, 30), "America/Sao_Paulo", "-03:00", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 40000);
        _sunset.Setup(s => s.GetSunsetTimeAsync(-28.2, -48.6, new DateOnly(2026, 10, 30), default)).ReturnsAsync(expected);

        Assert.Same(expected, await _sut.GetSunsetTimeAsync("imbituba-sc", new DateOnly(2026, 10, 30)));
    }

    [Fact]
    public async Task Search_MapsTheCitiesWithTheirCountsAndKeepsThePaging()
    {
        var query = new CityListQuery("imb", CityKindFilter.Municipality, null, null, CitySortOption.Locations, null, 20);
        _cities.Setup(r => r.SearchAsync(query, default))
            .ReturnsAsync(new CursorPagedResult<CityWithStats>([new CityWithStats(Municipality(), new CityStats(3, 9, 4, 4.25m, "https://img/c.jpg"))], "next", true));

        var page = await _sut.SearchAsync(query);

        var item = Assert.Single(page.Items);
        Assert.Equal("imbituba-sc", item.Slug);
        Assert.Equal(3, item.LocationsCount);
        Assert.Equal(4.25m, item.AvgRating);
        Assert.Equal("next", page.NextCursor);
        Assert.True(page.HasMore);
    }
}
