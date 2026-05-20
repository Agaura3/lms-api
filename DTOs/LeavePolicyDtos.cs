using System.ComponentModel.DataAnnotations;

namespace lms_api.DTOs;

public class CreateLeavePolicyRequest
{
    [Required, MaxLength(100)]
    public string LeaveTypeName { get; set; } = string.Empty;

    [Range(0, 365)]
    public int MaxDaysPerYear { get; set; }

    [Range(0, 365)]
    public int CarryForwardLimit { get; set; }
}

public class UpdateLeavePolicyRequest
{
    [Required, MaxLength(100)]
    public string LeaveTypeName { get; set; } = string.Empty;

    [Range(0, 365)]
    public int MaxDaysPerYear { get; set; }

    [Range(0, 365)]
    public int CarryForwardLimit { get; set; }

    public bool IsActive { get; set; } = true;
}
