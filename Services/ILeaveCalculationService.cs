using lms_api.Models;

namespace lms_api.Services;

public interface ILeaveCalculationService
{
    double CalculateLeaveDays(Leave leave);
}

public class LeaveCalculationService : ILeaveCalculationService
{
    public double CalculateLeaveDays(Leave leave)
    {
        if (leave.IsHalfDay)
            return 0.5;

        return (leave.EndDate.Date - leave.StartDate.Date).Days + 1;
    }
}
