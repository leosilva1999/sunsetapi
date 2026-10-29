using Sunset.Application.DTOs.Moderation;
using Sunset.Application.Validators.Moderation;

namespace Sunset.UnitTests.Validators;

public class UpdateLocationCoordinatesRequestValidatorTests
{
    private readonly UpdateLocationCoordinatesRequestValidator _sut = new();

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-90, 180)]
    [InlineData(90, -180)]
    [InlineData(-28.1, -48.6)]
    public void InRange_IsValid(double lat, double lng) =>
        Assert.True(_sut.Validate(new UpdateLocationCoordinatesRequest(lat, lng)).IsValid);

    [Theory]
    [InlineData(90.1, 0)]
    [InlineData(-90.1, 0)]
    [InlineData(0, 180.1)]
    [InlineData(0, -180.1)]
    public void OutOfRange_IsInvalid(double lat, double lng) =>
        Assert.False(_sut.Validate(new UpdateLocationCoordinatesRequest(lat, lng)).IsValid);
}
