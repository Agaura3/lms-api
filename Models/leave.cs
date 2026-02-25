using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using lms_api.Models.Enums;

namespace lms_api.Models;

public class Leave
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public DateTime StartDate { get; set; }

    [Required]
    public DateTime EndDate { get; set; }

    [Required]
    public string Reason { get; set; } = string.Empty;

    // 🔹 Enum instead of string (Industry level)
    [Required]
    public LeaveStatus Status { get; set; } = LeaveStatus.Pending;

    // 🔹 User Relation
    [Required]
    public Guid UserId { get; set; }

    [ForeignKey("UserId")]
    public User? User { get; set; }

    // 🔹 Company Relation (Multi-tenant)
    [Required]
    public Guid CompanyId { get; set; }

    [ForeignKey("CompanyId")]
    public Company? Company { get; set; }
    public LeaveType LeaveType { get; set; }
}