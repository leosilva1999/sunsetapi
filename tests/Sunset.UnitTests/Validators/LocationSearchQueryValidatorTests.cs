using Sunset.Application.DTOs.Locations;
using Sunset.Application.Validators.Locations;

namespace Sunset.UnitTests.Validators;

public class LocationSearchQueryValidatorTests
{
    private readonly LocationSearchQueryValidator _validator = new();

    private static LocationSearchQuery Query(double? lat = null, double? lng = null, double? radius = null) =>
        new(null, lat, lng, radius, null);

    [Fact]
    public void NoGeoParameters_IsValid() =>
        Assert.True(_validator.Validate(Query()).IsValid);

    [Theory]
    [InlineData(-27.6, -48.5, null)]
    [InlineData(-27.6, -48.5, 10.0)]
    [InlineData(-27.6, -48.5, LocationSearchQuery.MaxRadiusKm)]
    [InlineData(90, 180, 1.0)]
    public void ValidGeoParameters_AreAccepted(double lat, double lng, double? radius) =>
        Assert.True(_validator.Validate(Query(lat, lng, radius)).IsValid);

    [Theory]
    [InlineData(-27.6, null, null)]
    [InlineData(null, -48.5, null)]
    [InlineData(null, null, 10.0)]
    [InlineData(-27.6, null, 10.0)]
    [InlineData(null, -48.5, 10.0)]
    public void IncompleteGeoParameters_AreRejected(double? lat, double? lng, double? radius) =>
        Assert.False(_validator.Validate(Query(lat, lng, radius)).IsValid);

    [Theory]
    [InlineData(90.1, 0, null)]
    [InlineData(-90.1, 0, null)]
    [InlineData(0, 180.1, null)]
    [InlineData(0, -180.1, null)]
    [InlineData(0, 0, 0.0)]
    [InlineData(0, 0, -5.0)]
    [InlineData(0, 0, LocationSearchQuery.MaxRadiusKm + 1)]
    public void OutOfRangeValues_AreRejected(double lat, double lng, double? radius) =>
        Assert.False(_validator.Validate(Query(lat, lng, radius)).IsValid);
}
