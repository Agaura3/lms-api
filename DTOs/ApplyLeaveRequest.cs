using lms_api.Models.Enums;

namespace lms_api.DTOs;

public class ApplyLeaveRequest
{
    public LeaveType LeaveType { get; set; }

    public DateTime StartDate { get; set; }

    public DateTime EndDate { get; set; }

    public string Reason { get; set; } = string.Empty;
}