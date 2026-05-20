using FluentValidation;
using lms_api.Common;
using lms_api.DTOs;

namespace lms_api.Validators;

public class RegisterCompanyRequestValidator : AbstractValidator<RegisterCompanyRequest>
{
    public RegisterCompanyRequestValidator()
    {
        RuleFor(x => x.CompanyName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password)
            .Must(p => PasswordPolicy.IsValid(p, out _))
            .WithMessage("Password must be at least 8 characters with upper, lower, and a digit.");
    }
}
