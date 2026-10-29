using Sunset.Application.DTOs.Moderation;
using Sunset.Application.Validators.Moderation;

namespace Sunset.UnitTests.Validators;

public class AnonymizeUserRequestValidatorTests
{
    private readonly AnonymizeUserRequestValidator _sut = new();

    [Fact]
    public void WithReason_IsValid() =>
        Assert.True(_sut.Validate(new AnonymizeUserRequest("Conteudo ilegal")).IsValid);

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void EmptyReason_IsInvalid(string reason) =>
        Assert.False(_sut.Validate(new AnonymizeUserRequest(reason)).IsValid);

    [Fact]
    public void ReasonOver500Chars_IsInvalid() =>
        Assert.False(_sut.Validate(new AnonymizeUserRequest(new string('a', 501))).IsValid);
}
