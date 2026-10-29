using FluentValidation;
using Sunset.Application.DTOs.Legal;

namespace Sunset.Application.Validators.Legal;

public class AcceptLegalDocumentsRequestValidator : AbstractValidator<AcceptLegalDocumentsRequest>
{
    public AcceptLegalDocumentsRequestValidator()
    {
        RuleFor(x => x.TermsVersion).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PrivacyVersion).GreaterThanOrEqualTo(1);
    }
}
