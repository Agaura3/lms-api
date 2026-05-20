using lms_api.Data;
using lms_api.Models;

namespace lms_api.Services;

public interface IAuditService
{
    Task LogAsync(Guid userId, Guid companyId, string action, string entityName, Guid entityId);
}

public class AuditService : IAuditService
{
    private readonly AppDbContext _context;

    public AuditService(AppDbContext context) => _context = context;

    public async Task LogAsync(Guid userId, Guid companyId, string action, string entityName, Guid entityId)
    {
        _context.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            CompanyId = companyId,
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();
    }
}
