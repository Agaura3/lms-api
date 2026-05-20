using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using lms_api.Common;
using lms_api.Data;
using lms_api.Extensions;

namespace lms_api.Controllers;

[ApiController]
[Route("api/audit")]
[Authorize(Roles = "Admin")]
public class AuditController : ControllerBase
{
    private readonly AppDbContext _context;

    public AuditController(AppDbContext context) => _context = context;

    [HttpGet]
    public async Task<IActionResult> GetRecent([FromQuery] int limit = 50)
    {
        var companyId = User.GetCompanyId();
        if (companyId == null) return Unauthorized();

        limit = Math.Clamp(limit, 1, 200);

        var logs = await _context.AuditLogs
            .AsNoTracking()
            .Where(a => a.CompanyId == companyId)
            .OrderByDescending(a => a.CreatedAt)
            .Take(limit)
            .Join(
                _context.Users.AsNoTracking(),
                a => a.UserId,
                u => u.Id,
                (a, u) => new
                {
                    a.Id,
                    userName = u.FullName,
                    a.Action,
                    a.EntityName,
                    a.EntityId,
                    a.CreatedAt
                })
            .ToListAsync();

        return Ok(ApiResponse<object>.SuccessResponse(logs));
    }
}
