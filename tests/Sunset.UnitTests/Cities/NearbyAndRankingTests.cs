using FluentValidation;
using Moq;
using Sunset.Application.Cities;
using Sunset.Application.Common;
using Sunset.Application.DTOs.Cities;
using Sunset.Application.DTOs.Locations;
using Sunset.Application.Exceptions;
using Sunset.Application.Interfaces;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Application.Services;
using Sunset.Domain.Entities;
using Sunset.Domain.Enums;

namespace Sunset.UnitTests.Cities;

public class LocationNearbyAndRankingTests
{
    private readonly Mock<ILocationRepository> _locations = new();
    private readonly Mock<ICityRepository> _cities = new();
    private readonly LocationService _sut;

    public LocationNearbyAndRankingTests()
    {
        _locations.Setup(r => r.GetStatsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), default)).ReturnsAsync(new Dictionary<Guid, LocationStats>());
        _sut = new LocationService(_locations.Object, new Mock<IPhotoRepository>().Object, new Mock<ISunsetTimeService>().Object,
            _cities.Object, new CityResolver(_cities.Object), new Mock<IResponseCacheInvalidator>().Object);
    }

    private static Location Place(string name, double lat, double lng)
    {
        var location = new Location(name, lat, lng, "Imbituba - SC");
        location.AssignSlug(name.ToLowerInvariant().Replace(' ', '-'));
        return location;
    }

    // ---------------------------------------------------------------- nearby

    [Fact]
    public async Task Nearby_SearchesAroundTheLocation_AndLeavesItselfOut()
    {
        var rosa = Place("Rosa", -28.13, -48.65);
        var near = Place("Perto", -28.14, -48.66);
        var far = Place("Mais Longe", -28.2, -48.7);
        _locations.Setup(r => r.GetByIdAsync(rosa.Id, default)).ReturnsAsync(rosa);
        LocationSearchQuery? asked = null;
        _locations.Setup(r => r.SearchAsync(It.IsAny<LocationSearchQuery>(), default))
            .Callback((LocationSearchQuery q, CancellationToken _) => asked = q)
            .ReturnsAsync(new CursorPagedResult<LocationWithDistance>(
                [new LocationWithDistance(rosa, 0), new LocationWithDistance(near, 1.4), new LocationWithDistance(far, 9.8)], null, false));

        var result = await _sut.GetNearbyAsync(rosa.Id.ToString(), 30, 12);

        Assert.Equal(["perto", "mais-longe"], result.Select(r => r.Slug));
        Assert.Equal([1.4, 9.8], result.Select(r => r.DistanceKm));
        Assert.Equal((rosa.Latitude, rosa.Longitude, 30d, 13), (asked!.Latitude!.Value, asked.Longitude!.Value, asked.RadiusKm!.Value, asked.Limit));   // one spare row: the location finds itself
    }

    [Fact]
    public async Task Nearby_TakesASlug_CapsTheLimit_AndKeepsAtMostThatMany()
    {
        var rosa = Place("Rosa", -28.13, -48.65);
        _locations.Setup(r => r.GetBySlugAsync("rosa", default)).ReturnsAsync(rosa);
        var many = Enumerable.Range(0, 20).Select(i => new LocationWithDistance(Place($"L{i}", -28.1, -48.6), i)).ToList();
        _locations.Setup(r => r.SearchAsync(It.IsAny<LocationSearchQuery>(), default)).ReturnsAsync(new CursorPagedResult<LocationWithDistance>(many, null, false));

        var result = await _sut.GetNearbyAsync("rosa", 30, 500);

        Assert.Equal(LocationService.MaxNearbyLimit, result.Count);
        _locations.Verify(r => r.SearchAsync(It.Is<LocationSearchQuery>(q => q.Limit == LocationService.MaxNearbyLimit + 1), default), Times.Once);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(201)]
    [InlineData(double.NaN)]
    public async Task Nearby_RefusesARadiusOutsideZeroToTwoHundred(double radius) =>
        await Assert.ThrowsAsync<ValidationException>(() => _sut.GetNearbyAsync(Guid.NewGuid().ToString(), radius, 5));

    [Fact]
    public async Task Nearby_OfAnUnknownLocation_Is404()
    {
        _locations.Setup(r => r.GetBySlugAsync("nada", default)).ReturnsAsync((Location?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetNearbyAsync("nada", 30, 5));
    }

    // ---------------------------------------------------------------- ranking filters

    [Fact]
    public async Task Ranking_WithoutFilters_UsesTheOriginalQuery()
    {
        _locations.Setup(r => r.GetRankingAsync(RankingPeriod.Week, 10, default)).ReturnsAsync([]);

        await _sut.GetRankingAsync(RankingPeriod.Week, 10, null, "  ");

        _locations.Verify(r => r.GetRankingAsync(RankingPeriod.Week, 10, default), Times.Once);
        _locations.Verify(r => r.GetRankingAsync(It.IsAny<RankingPeriod>(), It.IsAny<int>(), It.IsAny<RankingFilter>(), default), Times.Never);
    }

    [Fact]
    public async Task Ranking_ByAMunicipalitySlug_FiltersOnItsId_AndByALocalitySlugOnTheLocality()
    {
        var imbituba = CityFixtures.Municipality();
        var rosa = CityFixtures.Locality(imbituba);
        _cities.Setup(r => r.GetBySlugAsync(imbituba.Slug, default)).ReturnsAsync(imbituba);
        _cities.Setup(r => r.GetBySlugAsync(rosa.Slug, default)).ReturnsAsync(rosa);
        _locations.Setup(r => r.GetRankingAsync(It.IsAny<RankingPeriod>(), It.IsAny<int>(), It.IsAny<RankingFilter>(), default)).ReturnsAsync([]);

        await _sut.GetRankingAsync(RankingPeriod.All, 5, imbituba.Slug, "sc");
        await _sut.GetRankingAsync(RankingPeriod.All, 5, rosa.Slug, null);

        _locations.Verify(r => r.GetRankingAsync(RankingPeriod.All, 5, It.Is<RankingFilter>(f => f.CityId == imbituba.Id && f.LocalityId == null && f.State == "SC"), default), Times.Once);
        _locations.Verify(r => r.GetRankingAsync(RankingPeriod.All, 5, It.Is<RankingFilter>(f => f.LocalityId == rosa.Id && f.CityId == null && f.State == null), default), Times.Once);
    }

    [Fact]
    public async Task Ranking_WithAnUnknownCityIs404_AndAMalformedStateIs400()
    {
        _cities.Setup(r => r.GetBySlugAsync("nada-xx", default)).ReturnsAsync((City?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetRankingAsync(RankingPeriod.All, 5, "nada-xx", null));
        await Assert.ThrowsAsync<ValidationException>(() => _sut.GetRankingAsync(RankingPeriod.All, 5, null, "S;C"));
        await Assert.ThrowsAsync<ValidationException>(() => _sut.GetRankingAsync(RankingPeriod.All, 5, null, "12"));
    }
}

public class CityRankingTests
{
    [Fact]
    public async Task TheCitiesRanking_CarriesThePeriodFigures_NextToTheUsualCounts()
    {
        var cities = new Mock<ICityRepository>();
        var imbituba = CityFixtures.Municipality();
        cities.Setup(r => r.GetRankingAsync(RankingPeriod.Month, 10, default))
            .ReturnsAsync([new CityRankingEntry(imbituba, new CityStats(4, 9, 12, 4.2m, null), 4.75m, 3)]);
        var sut = new CityService(cities.Object, new Mock<ILocationRepository>().Object, new Mock<IPhotoService>().Object, new Mock<ISunsetTimeService>().Object);

        var result = await sut.GetRankingAsync(RankingPeriod.Month, 10);

        var entry = Assert.Single(result);
        Assert.Equal(imbituba.Slug, entry.City.Slug);
        Assert.Equal(4, entry.City.LocationsCount);
        Assert.Equal((4.75m, 3), (entry.PeriodAvgRating, entry.PeriodRatingsCount));
    }
}
