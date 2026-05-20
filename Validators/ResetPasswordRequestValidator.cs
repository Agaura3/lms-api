using FluentValidation;
using lms_api.Common;
using lms_api.DTOs;

namespace lms_api.Validators;

public class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.NewPassword)
            .Must(p => PasswordPolicy.IsValid(p, out _))
            .WithMessage("Password must be at least 8 characters with upper, lower, and a digit.");
    }
}
