using lms_api.Models.Enums;
namespace lms_api.DTOs;

public class RegisterEmployeeRequest
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Department { get; set; } = "General";
    public UserRole Role { get; set; } = UserRole.Employee;
}