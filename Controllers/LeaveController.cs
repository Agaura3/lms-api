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
using StackExchange.Redis;

namespace lms_api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LeaveController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly IConnectionMultiplexer _redis;

    private IDatabase Cache => _redis.GetDatabase();

    public LeaveController(
        AppDbContext context,
        IHubContext<NotificationHub> hubContext,
        IConnectionMultiplexer redis)
    {
        _context = context;
        _hubContext = hubContext;
        _redis = redis;
    }

    private async Task ClearDashboardCache(Guid companyId)
    {
        var currentYear = DateTime.UtcNow.Year;

        await Cache.KeyDeleteAsync($"monthly_trends:{companyId}:{currentYear}");
        await Cache.KeyDeleteAsync($"dashboard:{companyId}:{currentYear}");
    }

    // rest of methods...


    // ===================================================
    // 🔹 Employee Apply Leave
    // ===================================================
    [Authorize(Policy = "ApplyLeave")]
    [HttpPost("apply")]
    public async Task<IActionResult> ApplyLeave(Leave request)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var companyId = Guid.Parse(User.FindFirst("CompanyId")!.Value);

        if (request.EndDate < request.StartDate)
            return BadRequest("Invalid leave dates.");

        var leaveDays = (request.EndDate - request.StartDate).Days + 1;

        var user = await _context.Users.FindAsync(userId);

        if (user == null)
            return NotFound("User not found.");

        if ((user.TotalLeaveBalance - user.UsedLeave) < leaveDays)
            return BadRequest("Insufficient leave balance.");

        var leave = new Leave
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CompanyId = companyId,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Reason = request.Reason,
            Status = LeaveStatus.Pending
        };

        _context.Leaves.Add(leave);

        var admins = await _context.Users
            .Where(u => u.CompanyId == companyId && u.Role == UserRole.Admin)
            .ToListAsync();

        foreach (var admin in admins)
        {
            var notification = new Notification
            {
                Id = Guid.NewGuid(),
                UserId = admin.Id,
                Title = "New Leave Application",
                Message = $"{user.FullName} applied for leave.",
                IsRead = false
            };

            _context.Notifications.Add(notification);

            await _hubContext.Clients.User(admin.Id.ToString())
                .SendAsync("ReceiveNotification", new
                {
                    notification.Title,
                    notification.Message,
                    type = "info"
                });
        }

        await _context.SaveChangesAsync();
        await ClearDashboardCache(companyId);

        return Ok("Leave applied successfully.");
    }

    // ===================================================
    // 🔹 Admin Approve Leave
    // ===================================================
    [Authorize(Policy = "ApproveLeave")]
    [HttpPut("approve/{id}")]
    public async Task<IActionResult> ApproveLeave(Guid id)
    {
        var companyId = Guid.Parse(User.FindFirst("CompanyId")!.Value);

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
    [Authorize(Policy = "RejectLeave")]
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
    [Authorize(Policy = "CancelLeave")]
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

        if (leave.Status != LeaveStatus.Approved)
            return BadRequest("Only approved leaves can be cancelled.");

        if (leave.StartDate <= DateTime.UtcNow.Date)
            return BadRequest("Cannot cancel started leave.");

        var leaveDays = (leave.EndDate - leave.StartDate).Days + 1;

        leave.User!.UsedLeave -= leaveDays;
        leave.Status = LeaveStatus.Cancelled;

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

        var totalEmployees = await _context.Users
            .CountAsync(u => u.CompanyId == companyId && u.Role == UserRole.Employee);

        var totalLeaves = await _context.Leaves
            .CountAsync(l => l.CompanyId == companyId);

        var pendingLeaves = await _context.Leaves
            .CountAsync(l => l.CompanyId == companyId && l.Status == LeaveStatus.Pending);

        var approvedLeaves = await _context.Leaves
            .CountAsync(l => l.CompanyId == companyId && l.Status == LeaveStatus.Approved);

        var rejectedLeaves = await _context.Leaves
            .CountAsync(l => l.CompanyId == companyId && l.Status == LeaveStatus.Rejected);

        return Ok(new
        {
            totalEmployees,
            totalLeaves,
            pendingLeaves,
            approvedLeaves,
            rejectedLeaves
        });
    }
}