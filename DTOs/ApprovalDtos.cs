namespace lms_api.DTOs;

public class BulkLeaveActionRequest
{
    public List<Guid> LeaveIds { get; set; } = new();
    public string? Comment { get; set; }
}
