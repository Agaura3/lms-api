using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using lms_api.Data;
using lms_api.Extensions;
using lms_api.Models.Enums;
using lms_api.Common;

namespace lms_api.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly AppDbContext _context;

    public DashboardController(AppDbContext context) => _context = context;

    [Authorize(Roles = "Manager")]
    [HttpGet("manager")]
    public async Task<IActionResult> GetManagerDashboard()
    {
        var managerId = User.GetUserId();
        var companyId = User.GetCompanyId();

        if (managerId == null || companyId == null)
            return Unauthorized(ApiResponse<string>.FailResponse("Invalid token"));

        var teamUserIds = await _context.Users
            .AsNoTracking()
            .Where(u => u.CompanyId == companyId && u.ManagerId == managerId)
            .Select(u => u.Id)
            .ToListAsync();

        var pendingRequests = await _context.Leaves
            .AsNoTracking()
            .CountAsync(l =>
                l.CompanyId == companyId &&
                l.Status == LeaveStatus.Pending &&
                teamUserIds.Contains(l.UserId));

        var usedLeaves = await _context.Users
            .AsNoTracking()
            .Where(u => teamUserIds.Contains(u.Id))
            .SumAsync(u => u.UsedLeave);

        var totalLeaves = await _context.Users
            .AsNoTracking()
            .Where(u => teamUserIds.Contains(u.Id))
            .SumAsync(u => u.TotalLeaveBalance);

        return Ok(ApiResponse<object>.SuccessResponse(new
        {
            totalLeaves,
            usedLeaves,
            pendingRequests,
            teamSize = teamUserIds.Count
        }));
    }
}
