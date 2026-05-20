using System.ComponentModel.DataAnnotations;

namespace lms_api.DTOs;

public class UpdateCompanySettingsRequest
{
    [Range(1, 365)]
    public int DefaultAnnualLeaveDays { get; set; } = 20;

    [MaxLength(100)]
    public string TimeZone { get; set; } = "UTC";

    [MaxLength(20)]
    public string DateFormat { get; set; } = "dd/MM/yyyy";

    public bool EmailNotificationsEnabled { get; set; } = true;
}
