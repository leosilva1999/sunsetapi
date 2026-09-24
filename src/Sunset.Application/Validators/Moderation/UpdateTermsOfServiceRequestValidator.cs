using FluentValidation;
using Sunset.Application.DTOs.Moderation;

namespace Sunset.Application.Validators.Moderation;

public class UpdateTermsOfServiceRequestValidator : AbstractValidator<UpdateTermsOfServiceRequest>
{
    public UpdateTermsOfServiceRequestValidator()
    {
        RuleFor(x => x.Content)
            .NotEmpty()
            .MaximumLength(20000);
    }
}
