using FluentValidation;
using Sunset.Application.DTOs.Devices;
using Sunset.Application.Push;

namespace Sunset.Application.Validators.Devices;

public class RegisterDeviceRequestValidator : AbstractValidator<RegisterDeviceRequest>
{
    public RegisterDeviceRequestValidator()
    {
        RuleFor(x => x.Token)
            .Must(PushTokens.IsValid)
            .WithMessage("'token' must be an Expo push token such as ExponentPushToken[xxxxxxxx].");

        RuleFor(x => x.Platform).IsInEnum();
        RuleFor(x => x.InstallId).NotEqual(Guid.Empty).When(x => x.InstallId is not null);
        RuleFor(x => x.AppVersion).MaximumLength(32);
        RuleFor(x => x.Locale).MaximumLength(32);
    }
}

public class RemoveDeviceRequestValidator : AbstractValidator<RemoveDeviceRequest>
{
    public RemoveDeviceRequestValidator()
    {
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.Token) || x.InstallId is not null)
            .WithName("body")
            .WithMessage("Send 'token' or 'installId'.");

        RuleFor(x => x.Token).MaximumLength(PushTokens.MaxLength).When(x => x.Token is not null);
        RuleFor(x => x.InstallId).NotEqual(Guid.Empty).When(x => x.InstallId is not null);
    }
}
