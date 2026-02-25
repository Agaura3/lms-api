namespace lms_api.DTOs;

public class RegisterCompanyRequest
{
    public string CompanyName { get; set; } = string.Empty;
    public string AdminName { get; set; } = string.Empty;
    public string AdminEmail { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}