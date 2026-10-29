using Sunset.Application.DTOs.Moderation;
using Sunset.Application.Validators.Moderation;

namespace Sunset.UnitTests.Validators;

public class MergeLocationsRequestValidatorTests
{
    private readonly MergeLocationsRequestValidator _sut = new();

    [Fact]
    public void WithTarget_IsValid() =>
        Assert.True(_sut.Validate(new MergeLocationsRequest(Guid.NewGuid())).IsValid);

    [Fact]
    public void EmptyTarget_IsInvalid() =>
        Assert.False(_sut.Validate(new MergeLocationsRequest(Guid.Empty)).IsValid);
}
