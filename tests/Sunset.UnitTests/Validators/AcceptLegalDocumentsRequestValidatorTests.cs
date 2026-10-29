using Sunset.Application.DTOs.Legal;
using Sunset.Application.Validators.Legal;

namespace Sunset.UnitTests.Validators;

public class AcceptLegalDocumentsRequestValidatorTests
{
    private readonly AcceptLegalDocumentsRequestValidator _sut = new();

    [Fact]
    public void PositiveVersions_AreValid() =>
        Assert.True(_sut.Validate(new AcceptLegalDocumentsRequest(2, 3)).IsValid);

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, -1)]
    public void ZeroOrNegativeVersions_AreInvalid(int terms, int privacy) =>
        Assert.False(_sut.Validate(new AcceptLegalDocumentsRequest(terms, privacy)).IsValid);
}
