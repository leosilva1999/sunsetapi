using Moq;
using Sunset.Application.DTOs.Cities;
using Sunset.Application.Exceptions;
using Sunset.Application.Interfaces;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Application.Services;
using Sunset.Application.Validators.Cities;
using Sunset.Domain.Enums;

namespace Sunset.UnitTests.Cities;

public class NearbyCitiesTests
{
    private readonly Mock<ICityRepository> _cities = new();
    private readonly CityService _sut;

    public NearbyCitiesTests()
    {
        _sut = new CityService(_cities.Object, new Mock<ILocationRepository>().Object, new Mock<IPhotoService>().Object, new Mock<ISunsetTimeService>().Object);
    }

    private static NearbyCityResponse Place(string name, double km) => new(Guid.NewGuid(), CityKind.Locality, name, "SC", name.ToLowerInvariant(), km, null);

    [Fact]
    public async Task WithoutAChosenCity_ReturnsTheRepositorysNearestPlaces_AsIs()
    {
        var nearest = new[] { Place("Rosa", 0.4), Place("Ibiraquera", 2.9) };
        _cities.Setup(r => r.GetNearestAsync(-28.13, -48.65, 3, default)).ReturnsAsync(nearest);

        var result = await _sut.GetNearbyAsync(new NearbyCitiesQuery(-28.13, -48.65, null, 3));

        Assert.Equal(nearest, result.Items);
        Assert.Null(result.ChosenCity);
        _cities.Verify(r => r.GetActiveByIdAsync(It.IsAny<Guid>(), default), Times.Never);
    }

    [Theory]
    [InlineData(-28.2, -48.7, false)]    // a few km from Imbituba's centre
    [InlineData(-23.5, -46.6, true)]     // Sao Paulo: ~ 600 km away
    public async Task WithAChosenCity_SaysHowFarThePinIs_AndWarnsBeyond150Km(double lat, double lng, bool farAway)
    {
        var imbituba = CityFixtures.Municipality(lat: -28.24, lng: -48.67);
        _cities.Setup(r => r.GetNearestAsync(lat, lng, 3, default)).ReturnsAsync([]);
        _cities.Setup(r => r.GetActiveByIdAsync(imbituba.Id, default)).ReturnsAsync(imbituba);

        var result = await _sut.GetNearbyAsync(new NearbyCitiesQuery(lat, lng, imbituba.Id, 3));

        Assert.Equal(farAway, result.ChosenCity!.FarAway);
        Assert.Equal(imbituba.Id, result.ChosenCity.Id);
        Assert.True(result.ChosenCity.DistanceKm > 0);
    }

    [Fact]
    public async Task AnUnknownChosenCity_Is404_AndTheLimitIsKeptInRange()
    {
        _cities.Setup(r => r.GetNearestAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<int>(), default)).ReturnsAsync([]);
        _cities.Setup(r => r.GetActiveByIdAsync(It.IsAny<Guid>(), default)).ReturnsAsync((Sunset.Domain.Entities.City?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetNearbyAsync(new NearbyCitiesQuery(1, 1, Guid.NewGuid(), 3)));

        await _sut.GetNearbyAsync(new NearbyCitiesQuery(1, 1, null, 500));
        _cities.Verify(r => r.GetNearestAsync(1, 1, NearbyCitiesQuery.MaxLimit, default), Times.Once);
    }

    [Fact]
    public void TheQueryNeedsBothCoordinatesInRange()
    {
        var validator = new NearbyCitiesQueryValidator();

        Assert.True(validator.Validate(new NearbyCitiesQuery(-28, -48, null, 3)).IsValid);
        Assert.False(validator.Validate(new NearbyCitiesQuery(null, -48, null, 3)).IsValid);
        Assert.False(validator.Validate(new NearbyCitiesQuery(-28, null, null, 3)).IsValid);
        Assert.False(validator.Validate(new NearbyCitiesQuery(91, 0, null, 3)).IsValid);
        Assert.False(validator.Validate(new NearbyCitiesQuery(0, -181, null, 3)).IsValid);
        Assert.False(validator.Validate(new NearbyCitiesQuery(0, 0, Guid.Empty, 3)).IsValid);
    }
}
