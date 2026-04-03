using lms_api.Models.Enums;

namespace lms_api.DTOs;

public class ApplyLeaveRequest
{
    public string LeaveType { get; set; } = string.Empty;

    public DateTime StartDate { get; set; }

    public DateTime EndDate { get; set; }

    public string Reason { get; set; } = string.Empty;

    public bool IsHalfDay { get; set; }

    public string? HalfDayType { get; set; }
}