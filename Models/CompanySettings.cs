namespace lms_api.Models;

public class CompanySettings
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public int DefaultAnnualLeaveDays { get; set; } = 20;
    public string TimeZone { get; set; } = "UTC";
    public string DateFormat { get; set; } = "dd/MM/yyyy";
    public bool EmailNotificationsEnabled { get; set; } = true;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public Company Company { get; set; } = null!;
}
