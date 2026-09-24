using FluentValidation;
using Sunset.Application.DTOs.Moderation;

namespace Sunset.Application.Validators.Moderation;

public class UpdateLegalDocumentRequestValidator : AbstractValidator<UpdateLegalDocumentRequest>
{
    public UpdateLegalDocumentRequestValidator()
    {
        RuleFor(x => x.Content)
            .NotEmpty()
            .MaximumLength(20000);
    }
}
