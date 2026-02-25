namespace lms_api.Models;

public class LeavePolicy
{
    public Guid Id { get; set; }

    public Guid CompanyId { get; set; }

    public string LeaveTypeName { get; set; } = string.Empty;

    public int MaxDaysPerYear { get; set; }

    public int CarryForwardLimit { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Company Company { get; set; } = null!;
}