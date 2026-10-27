using Sunset.Application.DTOs.Users;
using Sunset.Application.Validators.Users;

namespace Sunset.UnitTests.Validators;

public class ChangePasswordRequestValidatorTests
{
    private readonly ChangePasswordRequestValidator _validator = new();

    [Fact]
    public void ValidRequest_IsAccepted() =>
        Assert.True(_validator.Validate(new ChangePasswordRequest("old", "new-password-123")).IsValid);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyCurrentPassword_IsRejected(string current)
    {
        var result = _validator.Validate(new ChangePasswordRequest(current, "new-password-123"));

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ChangePasswordRequest.CurrentPassword));
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("1234567")]
    public void NewPasswordUnderEightCharacters_IsRejected(string newPassword)
    {
        var result = _validator.Validate(new ChangePasswordRequest("old", newPassword));

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ChangePasswordRequest.NewPassword));
    }

    [Fact]
    public void NewPasswordWithExactlyEightCharacters_IsAccepted() =>
        Assert.True(_validator.Validate(new ChangePasswordRequest("old", "12345678")).IsValid);
}
