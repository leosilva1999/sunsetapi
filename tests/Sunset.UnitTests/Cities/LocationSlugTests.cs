using Moq;
using Sunset.Application.Cities;
using Sunset.Application.Common;
using Sunset.Application.DTOs.Locations;
using Sunset.Application.Exceptions;
using Sunset.Application.Interfaces;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Application.Services;
using Sunset.Domain.Entities;

namespace Sunset.UnitTests.Cities;

public class LocationSlugGeneratorTests
{
    [Theory]
    [InlineData("Praia do Rosa", "imbituba-sc", "praia-do-rosa-imbituba-sc")]
    [InlineData("  Mirante São João!! ", "florianopolis-sc", "mirante-sao-joao-florianopolis-sc")]
    [InlineData("Praia", null, "praia")]
    [InlineData("???", "imbituba-sc", "local-imbituba-sc")]
    public void ForLocation_JoinsTheNameAndTheCitySlug(string name, string? city, string expected) =>
        Assert.Equal(expected, SlugGenerator.ForLocation(name, city));

    [Fact]
    public void ForLocation_NeverExceedsTheColumnSize() =>
        Assert.True(SlugGenerator.ForLocation(new string('a', 400), "imbituba-sc").Length <= SlugGenerator.LocationMaxLength);

    [Fact]
    public void WithSuffix_KeepsTheSuffixEvenWhenTheBaseIsAtTheLimit()
    {
        var baseSlug = new string('a', SlugGenerator.LocationMaxLength);
        var suffixed = SlugGenerator.WithSuffix(baseSlug, 12, SlugGenerator.LocationMaxLength);

        Assert.Equal(SlugGenerator.LocationMaxLength, suffixed.Length);
        Assert.EndsWith("-12", suffixed);
    }

    [Theory]
    [InlineData("praia-do-rosa-imbituba-sc", true)]
    [InlineData("Praia-1", true)]
    [InlineData("praia do rosa", false)]
    [InlineData("praia/../x", false)]
    [InlineData("", false)]
    public void IsWellFormed_AcceptsOnlyLettersDigitsAndHyphens(string value, bool expected) =>
        Assert.Equal(expected, SlugGenerator.IsWellFormed(value));
}

public class LocationSlugDomainTests
{
    [Fact]
    public void AssignSlug_SetsItOnce_AndRefusesAChange()
    {
        var location = new Location("Praia", -28, -48, "x");

        location.AssignSlug("praia");

        Assert.Equal("praia", location.Slug);
        Assert.Throws<InvalidOperationException>(() => location.AssignSlug("outra"));
    }
}

public class LocationSlugServiceTests
{
    private readonly Mock<ILocationRepository> _locations = new();
    private readonly Mock<ICityRepository> _cities = new();
    private readonly Guid _user = Guid.NewGuid();
    private readonly LocationService _sut;

    public LocationSlugServiceTests()
    {
        _locations.Setup(r => r.GetStatsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), default))
            .ReturnsAsync(new Dictionary<Guid, LocationStats>());
        _sut = new LocationService(_locations.Object, new Mock<IPhotoRepository>().Object, new Mock<ISunsetTimeService>().Object,
            _cities.Object, new CityResolver(_cities.Object), new Mock<IResponseCacheInvalidator>().Object);
    }

    [Fact]
    public async Task CreateAsync_GivesTheFirstFreeSlug_CountingRemovedLocationsToo()
    {
        var imbituba = CityFixtures.Municipality();
        _cities.Setup(r => r.GetActiveByIdAsync(imbituba.Id, default)).ReturnsAsync(imbituba);
        var taken = new HashSet<string> { "praia-do-rosa-imbituba-sc", "praia-do-rosa-imbituba-sc-2" };
        _locations.Setup(r => r.SlugExistsAsync(It.IsAny<string>(), default)).ReturnsAsync((string s, CancellationToken _) => taken.Contains(s));

        var response = await _sut.CreateAsync(_user, new CreateLocationRequest("Praia do Rosa", -28.13, -48.65, null, imbituba.Id));

        Assert.Equal("praia-do-rosa-imbituba-sc-3", response.Slug);
    }

    [Fact]
    public async Task GetByIdOrSlug_AGuidLooksUpById_AnythingElseBySlug_AndMisfitsAre404()
    {
        var location = new Location("Praia", -28, -48, "x");
        location.AssignSlug("praia-x");
        _locations.Setup(r => r.GetByIdAsync(location.Id, default)).ReturnsAsync(location);
        _locations.Setup(r => r.GetBySlugAsync("praia-x", default)).ReturnsAsync(location);

        Assert.Equal("praia-x", (await _sut.GetByIdOrSlugAsync(location.Id.ToString())).Slug);
        Assert.Equal(location.Id, (await _sut.GetByIdOrSlugAsync("Praia-X")).Id);
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetByIdOrSlugAsync("nao-existe"));
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetByIdOrSlugAsync("com espaco"));
        _locations.Verify(r => r.GetBySlugAsync("com espaco", default), Times.Never);
    }
}
