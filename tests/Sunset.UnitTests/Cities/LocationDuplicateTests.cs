using Moq;
using Sunset.Application.Cities;
using Sunset.Application.DTOs.Locations;
using Sunset.Application.Exceptions;
using Sunset.Application.Interfaces;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Application.Services;
using Sunset.Domain.Entities;

namespace Sunset.UnitTests.Cities;

public class LocationDuplicateTests
{
    private readonly Mock<ILocationRepository> _locations = new();
    private readonly Mock<ICityRepository> _cities = new();
    private readonly LocationService _sut;
    private readonly Guid _user = Guid.NewGuid();

    public LocationDuplicateTests()
    {
        _sut = new LocationService(_locations.Object, new Mock<IPhotoRepository>().Object, new Mock<ISunsetTimeService>().Object,
            _cities.Object, new CityResolver(_cities.Object), new Mock<IResponseCacheInvalidator>().Object);
    }

    private City Imbituba()
    {
        var city = CityFixtures.Municipality();
        _cities.Setup(r => r.GetActiveByIdAsync(city.Id, default)).ReturnsAsync(city);
        return city;
    }

    private static CreateLocationRequest Request(Guid cityId, bool confirm = false) =>
        new("Praia do Rosa", -28.13, -48.65, CityId: cityId, ConfirmDistinct: confirm);

    [Fact]
    public async Task WithCandidates_TheCreateIsA409_ListingThemInTheErrors_AndNothingIsSaved()
    {
        var city = Imbituba();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        _locations.Setup(r => r.FindDuplicateCandidateIdsAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<Guid?>(), It.IsAny<double>(), It.IsAny<int>(), default))
            .ReturnsAsync([first, second]);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => _sut.CreateAsync(_user, Request(city.Id)));

        Assert.Equal([first.ToString(), second.ToString()], ex.Errors!["candidateLocationIds"]);
        _locations.Verify(r => r.AddAsync(It.IsAny<Location>(), default), Times.Never);
    }

    [Fact]
    public async Task TheSearchIsScopedToTheMunicipality_UsesTheNormalizedName_AndA150MeterRadius()
    {
        var city = Imbituba();
        _locations.Setup(r => r.FindDuplicateCandidateIdsAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<Guid?>(), It.IsAny<double>(), It.IsAny<int>(), default))
            .ReturnsAsync([]);

        await _sut.CreateAsync(_user, new CreateLocationRequest("  Praia  do ROSÁ! ", -28.13, -48.65, CityId: city.Id));

        _locations.Verify(r => r.FindDuplicateCandidateIdsAsync(
            "praia do rosa", -28.13, -48.65, city.Id, 0.15, LocationService.MaxDuplicateCandidates, default), Times.Once);
    }

    [Fact]
    public async Task ConfirmDistinct_SkipsTheCheck_AndCreatesTheLocation()
    {
        var city = Imbituba();

        var created = await _sut.CreateAsync(_user, Request(city.Id, confirm: true));

        Assert.Equal("Praia do Rosa", created.Name);
        _locations.Verify(r => r.FindDuplicateCandidateIdsAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<Guid?>(), It.IsAny<double>(), It.IsAny<int>(), default), Times.Never);
        _locations.Verify(r => r.AddAsync(It.IsAny<Location>(), default), Times.Once);
    }

    [Fact]
    public async Task WithoutALinkedMunicipality_OnlyThePositionIsChecked()
    {
        _cities.Setup(r => r.FindMunicipalitiesAsync(It.IsAny<string>(), It.IsAny<string?>(), default)).ReturnsAsync([]);
        _cities.Setup(r => r.FindLocalitiesAsync(It.IsAny<string>(), It.IsAny<string?>(), default)).ReturnsAsync([]);
        _locations.Setup(r => r.FindDuplicateCandidateIdsAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<Guid?>(), It.IsAny<double>(), It.IsAny<int>(), default))
            .ReturnsAsync([]);

        await _sut.CreateAsync(_user, new CreateLocationRequest("Mirante", -15.5, -46.5, "Lugar Nenhum"));

        _locations.Verify(r => r.FindDuplicateCandidateIdsAsync("mirante", -15.5, -46.5, null, 0.15, LocationService.MaxDuplicateCandidates, default), Times.Once);
    }
}
