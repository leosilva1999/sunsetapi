using Sunset.Application.DTOs.Moderation;
using Sunset.Application.Validators.Moderation;

namespace Sunset.UnitTests.Validators;

public class BanUserRequestValidatorTests
{
    private readonly BanUserRequestValidator _sut = new();

    [Fact]
    public void PermanentBanWithReason_IsValid() =>
        Assert.True(_sut.Validate(new BanUserRequest("Spam")).IsValid);

    [Fact]
    public void FutureEnd_IsValid() =>
        Assert.True(_sut.Validate(new BanUserRequest("Spam", DateTime.UtcNow.AddDays(1))).IsValid);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyReason_IsInvalid(string reason) =>
        Assert.False(_sut.Validate(new BanUserRequest(reason)).IsValid);

    [Fact]
    public void ReasonOver500Chars_IsInvalid() =>
        Assert.False(_sut.Validate(new BanUserRequest(new string('a', 501))).IsValid);

    [Fact]
    public void PastEnd_IsInvalid() =>
        Assert.False(_sut.Validate(new BanUserRequest("Spam", DateTime.UtcNow.AddMinutes(-1))).IsValid);

    [Fact]
    public void EndBeyondTenYears_IsInvalid() =>
        Assert.False(_sut.Validate(new BanUserRequest("Spam", DateTime.UtcNow.AddYears(11))).IsValid);
}
