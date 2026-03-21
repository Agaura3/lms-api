namespace lms_api.DTOs;

public class EditLeaveRequest
{
    public DateTime StartDate { get; set; }

    public DateTime EndDate { get; set; }

    public string Reason { get; set; } = string.Empty;
}