namespace lms_api.DTOs;

public class AssignManagerRequest
{
    public Guid EmployeeId { get; set; }
    public Guid ManagerId { get; set; }
}