namespace lms_api.Models;

public class CompanySubscription
{
    public Guid Id { get; set; }

    public Guid CompanyId { get; set; }

    public Guid PlanId { get; set; }

    public DateTime StartDate { get; set; }

    public DateTime EndDate { get; set; }

    public bool IsActive { get; set; }

    public Company Company { get; set; } = null!;

    public Plan Plan { get; set; } = null!;
}