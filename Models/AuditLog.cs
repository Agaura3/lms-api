namespace lms_api.Models;

public class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }   // Who performed action
    public string Action { get; set; } = null!;

    public string EntityName { get; set; } = null!;
    public Guid EntityId { get; set; }

    public Guid CompanyId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}