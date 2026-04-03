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

    [Required]
    public LeaveStatus Status { get; set; } = LeaveStatus.Pending;

    [Required]
    public Guid UserId { get; set; }

    [ForeignKey("UserId")]
    public User? User { get; set; }

    [Required]
    public Guid CompanyId { get; set; }

    [ForeignKey("CompanyId")]
    public Company? Company { get; set; }

   public string LeaveType { get; set; } = string.Empty;

   public bool IsHalfDay { get; set; }

   public string? HalfDayType { get; set; } // Morning / Afternoon

   public string? Priority {get; set; }

   public string? ManagerComment { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
}