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
    }
}
