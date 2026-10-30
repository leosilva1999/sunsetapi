using Moq;
using Sunset.Application.Common;
using Sunset.Application.DTOs.Cities;
using Sunset.Application.Exceptions;
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
}

public class CityServiceTests
{
    private readonly Mock<ICityRepository> _repository = new();
    private readonly CityService _sut;

    public CityServiceTests() => _sut = new CityService(_repository.Object);

    private static City Municipality() =>
        new(CityKind.Municipality, "Imbituba", "Imbituba", null, "imbituba", "BR", "SC", "imbituba-sc", "geonames", 1, "4207304", null, -28.2, -48.6, "America/Sao_Paulo", 45286);

    [Fact]
    public async Task GetBySlug_ReturnsTheMunicipality()
    {
        _repository.Setup(r => r.GetBySlugAsync("imbituba-sc", default)).ReturnsAsync(Municipality());

        var response = await _sut.GetBySlugAsync("imbituba-sc");

        Assert.Equal("Imbituba", response.Name);
        Assert.Equal("SC", response.State);
        Assert.Equal("America/Sao_Paulo", response.TimeZoneId);
        Assert.Null(response.Parent);
    }

    [Fact]
    public async Task GetBySlug_UnknownSlug_IsNotFound()
    {
        _repository.Setup(r => r.GetBySlugAsync(It.IsAny<string>(), default)).ReturnsAsync((City?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetBySlugAsync("nada-xx"));
    }

    [Fact]
    public async Task GetBySlug_ALocality_HasNoPublicPageYet()
    {
        var parent = Municipality();
        var locality = new City(CityKind.Locality, "Praia do Rosa", "Praia do Rosa", null, "praia do rosa", "BR", "SC", "praia-do-rosa-imbituba-sc", "geonames", 2, null, parent.Id, 0, 0, null, 0);
        _repository.Setup(r => r.GetBySlugAsync("praia-do-rosa-imbituba-sc", default)).ReturnsAsync(locality);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetBySlugAsync("praia-do-rosa-imbituba-sc"));
    }

    [Fact]
    public async Task Search_MapsTheCitiesAndKeepsThePaging()
    {
        var query = new CityListQuery("imb", CityKindFilter.Municipality, null, null, CitySortOption.Population, null, 20);
        _repository.Setup(r => r.SearchAsync(query, default))
            .ReturnsAsync(new CursorPagedResult<City>([Municipality()], "next", true));

        var page = await _sut.SearchAsync(query);

        Assert.Equal("imbituba-sc", Assert.Single(page.Items).Slug);
        Assert.Equal("next", page.NextCursor);
        Assert.True(page.HasMore);
    }
}
