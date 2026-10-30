using FluentValidation;
using Moq;
using Sunset.Application.Cities;
using Sunset.Application.DTOs.Cities;
using Sunset.Application.DTOs.Locations;
using Sunset.Application.Interfaces;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Application.Services;
using Sunset.Domain.Entities;
using Sunset.Domain.Enums;

namespace Sunset.UnitTests.Cities;

internal static class CityFixtures
{
    public static City Municipality(string name = "Imbituba", string state = "SC", double lat = -28.2, double lng = -48.7, string? slug = null, string? sourceName = null) =>
        new(CityKind.Municipality, name, sourceName ?? name, null, name.ToLowerInvariant(), "BR", state, slug ?? $"{name.ToLowerInvariant().Replace(' ', '-')}-{state.ToLowerInvariant()}",
            "geonames", Random.Shared.NextInt64(1, 1_000_000), "4207304", null, lat, lng, "America/Sao_Paulo", 1000);

    public static City Locality(City parent, string name = "Praia do Rosa", double lat = -28.13, double lng = -48.65, string? alternateNames = null)
    {
        var locality = new City(CityKind.Locality, name, name, alternateNames, $"{name.ToLowerInvariant()} {alternateNames?.ToLowerInvariant()}".Trim(), "BR", parent.AdminArea,
            $"{name.ToLowerInvariant().Replace(' ', '-')}-{parent.Slug}", "geonames", Random.Shared.NextInt64(1, 1_000_000), null, parent.Id, lat, lng, "America/Sao_Paulo", 0);
        typeof(City).GetProperty(nameof(City.ParentCity))!.SetValue(locality, parent);
        return locality;
    }
}

public class LocationChangeCityTests
{
    [Fact]
    public void ChangeCity_TiesTheMunicipality_AndRewritesTheLegacyText()
    {
        var location = new Location("Praia do Rosa", -28.13, -48.65, "imbituba sc");
        var city = CityFixtures.Municipality();

        location.ChangeCity(city);

        Assert.Equal(city.Id, location.CityId);
        Assert.Null(location.LocalityId);
        Assert.Equal("Imbituba - SC", location.City);
    }

    [Fact]
    public void ChangeCity_WithALocality_KeepsBothAndTheMunicipalitysText()
    {
        var location = new Location("Praia", -28.13, -48.65, "x");
        var city = CityFixtures.Municipality();
        var locality = CityFixtures.Locality(city);

        location.ChangeCity(city, locality);

        Assert.Equal(city.Id, location.CityId);
        Assert.Equal(locality.Id, location.LocalityId);
        Assert.Equal("Imbituba - SC", location.City);
    }

    [Fact]
    public void ChangeCity_RejectsALocalityAsTheCity_AndALocalityFromAnotherMunicipality()
    {
        var location = new Location("Praia", -28.13, -48.65, "x");
        var city = CityFixtures.Municipality();
        var other = CityFixtures.Municipality("Garopaba");
        var locality = CityFixtures.Locality(city);

        Assert.Throws<ArgumentException>(() => location.ChangeCity(locality));
        Assert.Throws<ArgumentException>(() => location.ChangeCity(other, locality));
    }

    [Fact]
    public void ChangeCity_WithoutALocality_ClearsAPreviousOne()
    {
        var location = new Location("Praia", -28.13, -48.65, "x");
        var city = CityFixtures.Municipality();
        location.ChangeCity(city, CityFixtures.Locality(city));

        location.ChangeCity(city);

        Assert.Null(location.LocalityId);
    }

    [Fact]
    public void CanonicalCityText_IsCutAtTheColumnLimit() =>
        Assert.True(Location.CanonicalCityText(CityFixtures.Municipality(new string('x', 140))).Length <= 100);

    [Fact]
    public void TheCreatorIsRecorded()
    {
        var user = Guid.NewGuid();

        Assert.Equal(user, new Location("A", 0, 0, "x", user).CreatedByUserId);
        Assert.Null(new Location("A", 0, 0, "x").CreatedByUserId);
    }
}

public class CityResolverTests
{
    private readonly Mock<ICityRepository> _cities = new();
    private readonly CityResolver _sut;

    public CityResolverTests() => _sut = new CityResolver(_cities.Object);

    [Theory]
    [InlineData("Goiaminas - MG", "Goiaminas", "MG")]
    [InlineData("Santa Helena", "Santa Helena", null)]
    [InlineData("Imbituba/sc", "Imbituba", "SC")]
    [InlineData("Imbituba, SC", "Imbituba", "SC")]
    [InlineData("  Praia do Rosa  ", "Praia do Rosa", null)]
    [InlineData("Rio - Brasil", "Rio - Brasil", null)]
    public void Parse_SplitsTheTrailingState(string text, string name, string? state) =>
        Assert.Equal((name, state), CityResolver.Parse(text));

    [Fact]
    public async Task ASingleMunicipality_IsLinked()
    {
        var imbituba = CityFixtures.Municipality();
        _cities.Setup(r => r.FindMunicipalitiesAsync("imbituba", "SC", default)).ReturnsAsync([imbituba]);

        var result = await _sut.ResolveAsync("Imbituba - SC", -28.2, -48.7);

        Assert.Equal(CityResolutionKind.Linked, result.Kind);
        Assert.Same(imbituba, result.Municipality);
    }

    [Fact]
    public async Task Homonyms_AreSettledByThePin_WhenOneIsClearlyNearer()
    {
        var pr = CityFixtures.Municipality("Santa Helena", "PR", -24.86, -54.34);
        var sc = CityFixtures.Municipality("Santa Helena", "SC", -26.94, -49.02);
        var ma = CityFixtures.Municipality("Santa Helena", "MA", -2.23, -45.30);
        _cities.Setup(r => r.FindMunicipalitiesAsync("santa helena", null, default)).ReturnsAsync([pr, sc, ma]);

        var result = await _sut.ResolveAsync("Santa Helena", -24.9, -54.3);

        Assert.Equal(CityResolutionKind.Linked, result.Kind);
        Assert.Same(pr, result.Municipality);
    }

    [Fact]
    public async Task Homonyms_FarFromEveryCandidate_AreAmbiguous_WithTheNearestAsCandidates()
    {
        var pr = CityFixtures.Municipality("Santa Helena", "PR", -24.86, -54.34);
        var sc = CityFixtures.Municipality("Santa Helena", "SC", -26.94, -49.02);
        var ma = CityFixtures.Municipality("Santa Helena", "MA", -2.23, -45.30);
        var pb = CityFixtures.Municipality("Santa Helena", "PB", -6.70, -38.63);
        _cities.Setup(r => r.FindMunicipalitiesAsync("santa helena", null, default)).ReturnsAsync([pr, sc, ma, pb]);

        // a pin in the middle of nowhere (Atlantic, off Bahia)
        var result = await _sut.ResolveAsync("Santa Helena", -15, -35);

        Assert.Equal(CityResolutionKind.Ambiguous, result.Kind);
        Assert.Null(result.Municipality);
        Assert.Equal(3, result.Candidates.Count);
    }

    [Fact]
    public async Task Homonyms_WhereTwoAreAboutAsNear_AreAmbiguous()
    {
        var a = CityFixtures.Municipality("Vila Nova", "SC", -27.0, -49.0);
        var b = CityFixtures.Municipality("Vila Nova", "PR", -27.0, -49.4);
        _cities.Setup(r => r.FindMunicipalitiesAsync("vila nova", null, default)).ReturnsAsync([a, b]);

        var result = await _sut.ResolveAsync("Vila Nova", -27.0, -49.2);

        Assert.Equal(CityResolutionKind.Ambiguous, result.Kind);
    }

    [Fact]
    public async Task ANameOnlyALocalityHas_IsSuggestedNeverLinked()
    {
        var buritis = CityFixtures.Municipality("Buritis", "MG", -15.6, -46.4);
        var goias = CityFixtures.Locality(buritis, "Goiasminas", -15.5, -46.5, "Goiaminas");
        _cities.Setup(r => r.FindMunicipalitiesAsync("goiaminas", "MG", default)).ReturnsAsync([]);
        _cities.Setup(r => r.FindLocalitiesAsync("goiaminas", "MG", default)).ReturnsAsync([goias]);

        var result = await _sut.ResolveAsync("Goiaminas - MG", -15.5, -46.5);

        Assert.Equal(CityResolutionKind.Locality, result.Kind);
        Assert.Same(goias, result.Locality);
        Assert.Same(buritis, result.Municipality);
    }

    [Fact]
    public async Task ALocalityFarFromThePin_IsNotSuggested()
    {
        var buritis = CityFixtures.Municipality("Buritis", "MG", -15.6, -46.4);
        var goias = CityFixtures.Locality(buritis, "Goiasminas", -15.5, -46.5);
        _cities.Setup(r => r.FindMunicipalitiesAsync(It.IsAny<string>(), It.IsAny<string?>(), default)).ReturnsAsync([]);
        _cities.Setup(r => r.FindLocalitiesAsync(It.IsAny<string>(), It.IsAny<string?>(), default)).ReturnsAsync([goias]);

        Assert.Equal(CityResolutionKind.None, (await _sut.ResolveAsync("Goiasminas", -30, -51)).Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("x")]
    [InlineData("--")]
    public async Task UnusableText_ResolvesToNothing_WithoutTouchingTheCatalog(string? text)
    {
        Assert.Equal(CityResolutionKind.None, (await _sut.ResolveAsync(text, 0, 0)).Kind);
        _cities.Verify(r => r.FindMunicipalitiesAsync(It.IsAny<string>(), It.IsAny<string?>(), default), Times.Never);
    }
}

public class LocationServiceCityTests
{
    private readonly Mock<ILocationRepository> _locations = new();
    private readonly Mock<ICityRepository> _cities = new();
    private readonly LocationService _sut;
    private readonly Guid _user = Guid.NewGuid();

    public LocationServiceCityTests()
    {
        _locations.Setup(r => r.GetStatsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), default))
            .ReturnsAsync(new Dictionary<Guid, LocationStats>());
        _sut = new LocationService(_locations.Object, new Mock<IPhotoRepository>().Object, new Mock<ISunsetTimeService>().Object,
            _cities.Object, new CityResolver(_cities.Object));
    }

    private Location? Saved() => _locations.Invocations
        .Where(i => i.Method.Name == nameof(ILocationRepository.AddAsync)).Select(i => (Location)i.Arguments[0]).SingleOrDefault();

    [Fact]
    public async Task WithACityId_TheLocationIsTiedAndNamedAfterTheCity_AndTextIsIgnored()
    {
        var imbituba = CityFixtures.Municipality();
        _cities.Setup(r => r.GetActiveByIdAsync(imbituba.Id, default)).ReturnsAsync(imbituba);

        var response = await _sut.CreateAsync(_user, new CreateLocationRequest("Praia do Rosa", -28.13, -48.65, "texto qualquer", imbituba.Id));

        Assert.Equal("Imbituba - SC", response.City);
        Assert.Equal("imbituba-sc", response.CitySlug);
        Assert.Equal("Imbituba", response.CityName);
        Assert.Equal("SC", response.State);
        Assert.Null(response.LocalitySlug);
        Assert.Equal(_user, Saved()!.CreatedByUserId);
        Assert.Equal(imbituba.Id, Saved()!.CityId);
        _cities.Verify(r => r.FindMunicipalitiesAsync(It.IsAny<string>(), It.IsAny<string?>(), default), Times.Never);
    }

    [Fact]
    public async Task WithALocalityId_TheMunicipalityIsItsParent()
    {
        var imbituba = CityFixtures.Municipality();
        var rosa = CityFixtures.Locality(imbituba);
        _cities.Setup(r => r.GetActiveByIdAsync(rosa.Id, default)).ReturnsAsync(rosa);

        var response = await _sut.CreateAsync(_user, new CreateLocationRequest("Ponto", -28.13, -48.65, CityId: rosa.Id));

        Assert.Equal(imbituba.Id, Saved()!.CityId);
        Assert.Equal(rosa.Id, Saved()!.LocalityId);
        Assert.Equal("imbituba-sc", response.CitySlug);
        Assert.Equal("praia-do-rosa-imbituba-sc", response.LocalitySlug);
        Assert.Equal("Praia do Rosa", response.LocalityName);
    }

    [Fact]
    public async Task AnUnknownOrInactiveCityId_IsAValidationError_AndNothingIsSaved()
    {
        _cities.Setup(r => r.GetActiveByIdAsync(It.IsAny<Guid>(), default)).ReturnsAsync((City?)null);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.CreateAsync(_user, new CreateLocationRequest("X", 0, 0, CityId: Guid.NewGuid())));

        Assert.Contains(ex.Errors, e => e.PropertyName == "CityId");
        _locations.Verify(r => r.AddAsync(It.IsAny<Location>(), default), Times.Never);
    }

    [Fact]
    public async Task OnlyText_ThatMatchesOneMunicipality_IsLinkedAndNormalised()
    {
        var imbituba = CityFixtures.Municipality();
        _cities.Setup(r => r.FindMunicipalitiesAsync("imbituba", null, default)).ReturnsAsync([imbituba]);

        var response = await _sut.CreateAsync(_user, new CreateLocationRequest("Praia", -28.13, -48.65, "imbituba"));

        Assert.Equal("Imbituba - SC", response.City);
        Assert.Equal(imbituba.Id, Saved()!.CityId);
    }

    [Fact]
    public async Task OnlyText_ThatIsAmbiguousOrUnknown_IsKeptAsTyped_AndNotLinked()
    {
        _cities.Setup(r => r.FindMunicipalitiesAsync(It.IsAny<string>(), It.IsAny<string?>(), default)).ReturnsAsync([]);
        _cities.Setup(r => r.FindLocalitiesAsync(It.IsAny<string>(), It.IsAny<string?>(), default)).ReturnsAsync([]);

        var response = await _sut.CreateAsync(_user, new CreateLocationRequest("Praia", -28.13, -48.65, "  Lugar Nenhum  "));

        Assert.Equal("Lugar Nenhum", response.City);
        Assert.Null(response.CitySlug);
        Assert.Null(Saved()!.CityId);
        Assert.Equal(_user, Saved()!.CreatedByUserId);
    }

    [Fact]
    public async Task OnlyText_ThatOnlyALocalityBears_IsNotLinkedOnItsOwn()
    {
        var buritis = CityFixtures.Municipality("Buritis", "MG", -15.6, -46.4);
        var goias = CityFixtures.Locality(buritis, "Goiasminas", -15.5, -46.5, "Goiaminas");
        _cities.Setup(r => r.FindMunicipalitiesAsync(It.IsAny<string>(), It.IsAny<string?>(), default)).ReturnsAsync([]);
        _cities.Setup(r => r.FindLocalitiesAsync("goiaminas", "MG", default)).ReturnsAsync([goias]);

        var response = await _sut.CreateAsync(_user, new CreateLocationRequest("Ribanceira", -15.5, -46.5, "Goiaminas - MG"));

        Assert.Equal("Goiaminas - MG", response.City);   // unchanged: confirming a locality changes what is displayed
        Assert.Null(Saved()!.CityId);
    }

    [Fact]
    public async Task SearchResponses_CarryTheCountsAndCover()
    {
        var location = new Location("Praia do Rosa", -28.13, -48.65, "Imbituba - SC");
        _locations.Setup(r => r.SearchAsync(It.IsAny<LocationSearchQuery>(), default))
            .ReturnsAsync(new Sunset.Application.Common.CursorPagedResult<LocationWithDistance>([new LocationWithDistance(location, 1.234)], null, false));
        _locations.Setup(r => r.GetStatsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), default))
            .ReturnsAsync(new Dictionary<Guid, LocationStats> { [location.Id] = new(3, 7, "https://img/cover.jpg") });

        var page = await _sut.SearchAsync(new LocationSearchQuery(null, null, null, null, null));

        var item = Assert.Single(page.Items);
        Assert.Equal(3, item.RatingsCount);
        Assert.Equal(7, item.PhotosCount);
        Assert.Equal("https://img/cover.jpg", item.CoverImageUrl);
        Assert.Equal(1.23, item.DistanceKm);
    }
}
