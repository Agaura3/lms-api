using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using lms_api.Data;
using lms_api.Models;
using lms_api.Models.Enums;
using lms_api.Hubs;
using Microsoft.AspNetCore.RateLimiting;
using lms_api.DTOs;

namespace lms_api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LeaveController : ControllerBase
{
private readonly AppDbContext _context;
private readonly IHubContext<NotificationHub> _hubContext;


public LeaveController(  
    AppDbContext context,  
    IHubContext<NotificationHub> hubContext)  
{  
    _context = context;  
    _hubContext = hubContext;  
}  

private async Task ClearDashboardCache(Guid companyId)  
{  
    // Redis removed – no caching implemented for now  
    await Task.CompletedTask;  
}  




    // rest of methods...


    // ===================================================
    // 🔹 Employee Apply Leave
    // ===================================================
    [Authorize]
[HttpPost("apply")]
public async Task<IActionResult> ApplyLeave(ApplyLeaveRequest request)
{
    try
    {
        // Get userId safely
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized("Invalid user token.");

        // Get companyId safely
        var companyIdClaim = User.FindFirst("CompanyId")?.Value;
        if (!Guid.TryParse(companyIdClaim, out var companyId))
            return Unauthorized("Invalid company token.");

        // Validate dates
        if (request.EndDate < request.StartDate)
            return BadRequest("Invalid leave dates.");

        var leaveDays = (request.EndDate - request.StartDate).Days + 1;

        // Get user
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
            return NotFound("User not found.");

        // Check leave balance
        if ((user.TotalLeaveBalance - user.UsedLeave) < leaveDays)
            return BadRequest("Insufficient leave balance.");

        // Create leave
        var leave = new Leave
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CompanyId = companyId,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Reason = request.Reason,
            LeaveType = request.LeaveType,
            Status = LeaveStatus.Pending
        };

        _context.Leaves.Add(leave);

        // Notify managers
        var managers = await _context.Users
            .Where(u => u.CompanyId == companyId && u.Role == UserRole.Manager)
            .ToListAsync();

        foreach (var manager in managers)
        {
            var notification = new Notification
            {
                Id = Guid.NewGuid(),
                UserId = manager.Id,
                Title = "New Leave Application",
                Message = $"{user.FullName} applied for leave.",
                IsRead = false
            };

            _context.Notifications.Add(notification);

            await _hubContext.Clients.User(manager.Id.ToString())
                .SendAsync("ReceiveNotification", new
                {
                    notification.Title,
                    notification.Message,
                    type = "info"
                });
        }

        await _context.SaveChangesAsync();

        await ClearDashboardCache(companyId);

        return Ok(new
        {
            Success = true,
            Message = "Leave applied successfully."
        });
    }
    catch (Exception ex)
    {
        return StatusCode(500, new
        {
            Success = false,
            Message = ex.Message
        });
    }
}
    // ===================================================
    // 🔹 Manager Approve Leave
    // ===================================================
    [Authorize(Roles = "Manager")]
[HttpPut("approve/{id}")]
    public async Task<IActionResult> ApproveLeave(Guid id)
    {
        var companyIdClaim = User.FindFirst("CompanyId")?.Value;
            if (!Guid.TryParse(companyIdClaim, out var companyId))
    return Unauthorized("Invalid company id");

        var leave = await _context.Leaves
            .Include(l => l.User)
            .FirstOrDefaultAsync(l => l.Id == id && l.CompanyId == companyId);

        if (leave == null)
            return NotFound("Leave not found.");

        if (leave.Status != LeaveStatus.Pending)
            return BadRequest("Leave already processed.");

        var leaveDays = (leave.EndDate - leave.StartDate).Days + 1;

        if ((leave.User!.TotalLeaveBalance - leave.User.UsedLeave) < leaveDays)
            return BadRequest("Insufficient leave balance.");

        leave.User.UsedLeave += leaveDays;
        leave.Status = LeaveStatus.Approved;

        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = leave.UserId,
            Title = "Leave Approved",
            Message = "Your leave has been approved.",
            IsRead = false
        };

        _context.Notifications.Add(notification);

        await _context.SaveChangesAsync();
        await ClearDashboardCache(companyId);

        await _hubContext.Clients.User(leave.UserId.ToString())
            .SendAsync("ReceiveNotification", new
            {
                notification.Title,
                notification.Message,
                type = "success"
            });

        return Ok("Leave approved successfully.");
    }

    // ===================================================
    // 🔹 Admin Reject Leave
    // ===================================================
    [Authorize(Roles = "Manager")]
[HttpPut("reject/{id}")]   
 public async Task<IActionResult> RejectLeave(Guid id)
    {
        var companyId = Guid.Parse(User.FindFirst("CompanyId")!.Value);

        var leave = await _context.Leaves
            .FirstOrDefaultAsync(l => l.Id == id && l.CompanyId == companyId);

        if (leave == null)
            return NotFound("Leave not found.");

        if (leave.Status != LeaveStatus.Pending)
            return BadRequest("Leave already processed.");

        leave.Status = LeaveStatus.Rejected;

        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = leave.UserId,
            Title = "Leave Rejected",
            Message = "Your leave has been rejected.",
            IsRead = false
        };

        _context.Notifications.Add(notification);

        await _context.SaveChangesAsync();
        await ClearDashboardCache(companyId);

        await _hubContext.Clients.User(leave.UserId.ToString())
            .SendAsync("ReceiveNotification", new
            {
                notification.Title,
                notification.Message,
                type = "error"
            });

        return Ok("Leave rejected successfully.");
    }

    // ===================================================
    // 🔹 Employee Cancel Leave
    // ===================================================
    [Authorize]
    [HttpPut("cancel/{id}")]
    public async Task<IActionResult> CancelLeave(Guid id)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var companyId = Guid.Parse(User.FindFirst("CompanyId")!.Value);

        var leave = await _context.Leaves
            .Include(l => l.User)
            .FirstOrDefaultAsync(l => l.Id == id && l.UserId == userId);

        if (leave == null)
            return NotFound("Leave not found.");

        if (leave.Status != LeaveStatus.Pending)
            return BadRequest("Only pending leaves can be cancelled.");

        if (leave.StartDate <= DateTime.UtcNow.Date)
            return BadRequest("Cannot cancel started leave.");

        var leaveDays = (leave.EndDate - leave.StartDate).Days + 1;

        leave.Status = LeaveStatus.Cancelled;
        leave.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        await ClearDashboardCache(companyId);

        return Ok("Leave cancelled successfully.");
    }

    // ===================================================
    // 🔹 Admin Dashboard Summary
    // ===================================================
    [Authorize(Policy = "ViewDashboard")]
[HttpGet("dashboard-summary")]
public async Task<IActionResult> GetDashboardSummary()
{
    var companyId = Guid.Parse(User.FindFirst("CompanyId")!.Value);

    var employees = await _context.Users
        .CountAsync(u => u.CompanyId == companyId && u.Role == UserRole.Employee);

    var leaves = await _context.Leaves
        .Where(l => l.CompanyId == companyId)
        .GroupBy(l => 1)
        .Select(g => new
        {
            totalLeaves = g.Count(),
            pendingLeaves = g.Count(x => x.Status == LeaveStatus.Pending),
            approvedLeaves = g.Count(x => x.Status == LeaveStatus.Approved),
            rejectedLeaves = g.Count(x => x.Status == LeaveStatus.Rejected)
        })
        .FirstOrDefaultAsync();

    return Ok(new
    {
        totalEmployees = employees,
        totalLeaves = leaves?.totalLeaves ?? 0,
        pendingLeaves = leaves?.pendingLeaves ?? 0,
        approvedLeaves = leaves?.approvedLeaves ?? 0,
        rejectedLeaves = leaves?.rejectedLeaves ?? 0
    });
}

   // ===================================================
// 🔹 Employee My Leaves
// ===================================================
[Authorize]
[HttpGet("my-leaves")]
public async Task<IActionResult> GetMyLeaves()
{
    var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    if (string.IsNullOrEmpty(userIdClaim))
        return Unauthorized("Invalid token");

    var userId = Guid.Parse(userIdClaim);

    var leaves = await _context.Leaves
        .Where(l => l.UserId == userId)
        .OrderByDescending(l => l.StartDate)
        .Select(l => new
        {
            l.Id,
            l.StartDate,
            l.EndDate,
            l.Reason,
            l.Status,
            l.LeaveType
        })
        .ToListAsync();

    return Ok(leaves);
}

// ===================================================
// 🔹 Employee Dashboard
// ===================================================
[Authorize]
[HttpGet("employee-dashboard")]
public async Task<IActionResult> GetEmployeeDashboard()
{
    try
    {
        // get user id from JWT
        var userIdClaim = User.Claims
            .FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier || c.Type == "nameid")
            ?.Value;

        if (!Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized("Invalid token");

        // get user info
        var user = await _context.Users.FirstOrDefaultAsync(x => x.Id == userId);

        if (user == null)
            return NotFound("User not found");

        // fetch leaves
        var leaves = await _context.Leaves
            .Where(l => l.UserId == userId)
            .ToListAsync();

        var pending = leaves.Count(l => l.Status == LeaveStatus.Pending);
        var approved = leaves.Count(l => l.Status == LeaveStatus.Approved);
        var rejected = leaves.Count(l => l.Status == LeaveStatus.Rejected);

        var remaining = user.TotalLeaveBalance - user.UsedLeave;

        return Ok(new
        {
            pending,
            approved,
            rejected,
            remaining
        });
    }
    catch (Exception ex)
    {
        return StatusCode(500, new
        {
            error = ex.Message
        });
    }
}
// ===================================================
// 🔹 Edit Leave
// ===================================================
[Authorize]
[HttpPut("edit/{id}")]
public async Task<IActionResult> EditLeave(Guid id, Leave request)
{
    var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    if (string.IsNullOrEmpty(userIdClaim))
        return Unauthorized("Invalid token");

    var userId = Guid.Parse(userIdClaim);

    var leave = await _context.Leaves
        .FirstOrDefaultAsync(l => l.Id == id && l.UserId == userId);

    if (leave == null)
        return NotFound("Leave not found.");

    if (leave.Status != LeaveStatus.Pending)
        return BadRequest("Only pending leaves can be edited.");

    leave.StartDate = request.StartDate;
    leave.EndDate = request.EndDate;
    leave.Reason = request.Reason;

    await _context.SaveChangesAsync();

    return Ok("Leave updated successfully.");
}

/// <summary>
/// Pending Approvals API
/// </summary>
/// 

[Authorize(Roles = "Manager")]
[HttpGet("pending-approvals")]
public async Task<IActionResult> GetPendingLeaves()
{
    var companyId = Guid.Parse(User.FindFirst("CompanyId")!.Value);

    var leaves = await _context.Leaves
        .Include(l => l.User)
        .Where(l => l.CompanyId == companyId && l.Status == LeaveStatus.Pending)
        .Select(l => new
        {
            l.Id,
            employee = l.User!.FullName,
            l.LeaveType,
            l.StartDate,
            l.EndDate,
            l.Reason
        })
        .ToListAsync();

    return Ok(leaves);
}


/// <summary>
/// Manager Leaves Filter API
/// </summary>
/// 
//

[Authorize(Roles = "Manager")]
[HttpGet("manager-leaves")]
public async Task<IActionResult> GetManagerLeaves(string status)
{
    var companyId = Guid.Parse(User.FindFirst("CompanyId")!.Value);

    var leaves = await _context.Leaves
        .Include(l => l.User)
        .Where(l => l.CompanyId == companyId && l.Status.ToString() == status)
        .Select(l => new
        {
            l.Id,
            employee = l.User!.FullName,
            l.LeaveType,
            l.StartDate,
            l.EndDate,
            l.Status
        })
        .ToListAsync();

    return Ok(leaves);
}
}