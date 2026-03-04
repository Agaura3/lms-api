namespace lms_api.DTOs;

public class RegisterCompanyRequest
{
    public string CompanyName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;   // 👈 added
    public string Email { get; set; } = string.Empty;  // 👈 changed from AdminEmail
    public string Password { get; set; } = string.Empty;
}