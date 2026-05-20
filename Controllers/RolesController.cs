using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using lms_api.Common;
using lms_api.Data;
using lms_api.Extensions;

namespace lms_api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class RolesController : ControllerBase
{
    private readonly AppDbContext _context;

    public RolesController(AppDbContext context) => _context = context;

    [HttpGet]
    public IActionResult GetSystemRoles()
    {
        var roles = new[]
        {
            new { name = "Admin", description = "Full company administration" },
            new { name = "Manager", description = "Approve team leave requests" },
            new { name = "Employee", description = "Apply and manage own leave" }
        };

        return Ok(ApiResponse<object>.SuccessResponse(roles));
    }

    [HttpGet("permissions")]
    public async Task<IActionResult> GetPermissions()
    {
        var companyId = User.GetCompanyId();
        if (companyId == null) return Unauthorized();

        var permissions = await _context.RolePermissions
            .AsNoTracking()
            .OrderBy(p => p.RoleName)
            .ThenBy(p => p.PermissionName)
            .Select(p => new { p.RoleName, p.PermissionName })
            .ToListAsync();

        return Ok(ApiResponse<object>.SuccessResponse(permissions));
    }
}
