using FluentValidation;
using Sunset.Application.DTOs.Photos;

namespace Sunset.Application.Validators.Photos;

public class CreateCommentRequestValidator : AbstractValidator<CreateCommentRequest>
{
    public CreateCommentRequestValidator()
    {
        RuleFor(x => x.Content)
            .NotEmpty()
            .MaximumLength(1000);

        RuleFor(x => x.ParentCommentId)
            .Must(id => id != Guid.Empty)
            .WithMessage("ParentCommentId must not be an empty GUID.")
            .When(x => x.ParentCommentId.HasValue);
    }
}
