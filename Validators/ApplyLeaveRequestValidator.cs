using FluentValidation;
using lms_api.DTOs;

namespace lms_api.Validators;

public class ApplyLeaveRequestValidator : AbstractValidator<ApplyLeaveRequest>
{
    public ApplyLeaveRequestValidator()
    {
        RuleFor(x => x.LeaveType).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Reason).NotEmpty().MinimumLength(10).MaximumLength(500);
        RuleFor(x => x.EndDate).GreaterThanOrEqualTo(x => x.StartDate)
            .When(x => !x.IsHalfDay);
        RuleFor(x => x.HalfDayType).NotEmpty().When(x => x.IsHalfDay);
    }
}
