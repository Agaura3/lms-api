using FluentValidation;
using lms_api.Common;
using lms_api.DTOs;

namespace lms_api.Validators;

public class RegisterEmployeeRequestValidator : AbstractValidator<RegisterEmployeeRequest>
{
    public RegisterEmployeeRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Department).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Password)
            .Must(p => PasswordPolicy.IsValid(p, out _))
            .WithMessage("Password must be at least 8 characters with upper, lower, and a digit.");
    }
}
