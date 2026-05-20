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
using lms_api.Common;
using lms_api.Extensions;
using lms_api.Services;

namespace lms_api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LeaveController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly ILeaveCalculationService _leaveCalc;
    private readonly IAuditService _audit;
    private readonly IDashboardBroadcastService _dashboardBroadcast;

    public LeaveController(
        AppDbContext context,
        IHubContext<NotificationHub> hubContext,
        ILeaveCalculationService leaveCalc,
        IAuditService audit,
        IDashboardBroadcastService dashboardBroadcast)
    {
        _context = context;
        _hubContext = hubContext;
        _leaveCalc = leaveCalc;
        _audit = audit;
        _dashboardBroadcast = dashboardBroadcast;
    }

    private Task NotifyDataChangedAsync(Guid companyId) =>
        _dashboardBroadcast.NotifyCompanyDataChangedAsync(companyId);

    private async Task<bool> ManagerCanAccessEmployeeAsync(Guid managerId, Guid companyId, Guid employeeUserId)
    {
        var employee = await _context.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == employeeUserId && u.CompanyId == companyId);

        if (employee == null) return false;
        if (employee.ManagerId.HasValue)
            return employee.ManagerId == managerId;

        return true;
    }




    // rest of methods...


    // ===================================================
    // 🔹 Employee Apply Leave
    // ===================================================
    [Authorize]
    [HttpPost("apply")]
    public async Task<IActionResult> ApplyLeave(ApplyLeaveRequest request)
    {
        var userId = User.GetUserId();
        var companyId = User.GetCompanyId();
        if (userId == null || companyId == null)
            return Unauthorized(ApiResponse<string>.FailResponse("Invalid token"));

        if (request.EndDate < request.StartDate)
            return BadRequest(ApiResponse<string>.FailResponse("Invalid leave dates"));

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
            return NotFound(ApiResponse<string>.FailResponse("User not found"));

        var leave = new Leave
        {
            Id = Guid.NewGuid(),
            UserId = userId.Value,
            CompanyId = companyId.Value,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Reason = request.Reason,
            LeaveType = request.LeaveType,
            IsHalfDay = request.IsHalfDay,
            HalfDayType = request.HalfDayType,
            Status = LeaveStatus.Pending
        };

        var leaveDays = _leaveCalc.CalculateLeaveDays(leave);
        if ((user.TotalLeaveBalance - user.UsedLeave) < leaveDays)
            return BadRequest(ApiResponse<string>.FailResponse("Insufficient leave balance"));

        _context.Leaves.Add(leave);

        var managersQuery = _context.Users
            .Where(u => u.CompanyId == companyId && u.Role == UserRole.Manager);

        if (user.ManagerId.HasValue)
            managersQuery = managersQuery.Where(u => u.Id == user.ManagerId);

        var managers = await managersQuery.ToListAsync();

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
        }

        await _context.SaveChangesAsync();

        foreach (var manager in managers)
        {
            await _hubContext.Clients.User(manager.Id.ToString())
                .SendAsync("ReceiveNotification", new
                {
                    Title = "New Leave Application",
                    Message = $"{user.FullName} applied for leave.",
                    type = "info"
                });
        }

        await _audit.LogAsync(userId.Value, companyId.Value, "APPLY", "Leave", leave.Id);
        await NotifyDataChangedAsync(companyId.Value);
        return Ok(ApiResponse<object>.SuccessResponse(new { leave.Id }, "Leave applied successfully"));
    }
    // ===================================================
    // 🔹 Manager Approve Leave
    // ===================================================
    [Authorize(Roles = "Manager")]
[HttpPut("approve/{id}")]
public async Task<IActionResult> ApproveLeave(Guid id, LeaveAction dto)
{
    var companyIdClaim = User.FindFirst("CompanyId")?.Value;
    if (!Guid.TryParse(companyIdClaim, out var companyId))
        return Unauthorized("Invalid company id");

    var leave = await _context.Leaves
        .Include(l => l.User)
        .FirstOrDefaultAsync(l => l.Id == id && l.CompanyId == companyId);

    if (leave == null)
        return NotFound(ApiResponse<string>.FailResponse("Leave not found"));

    var managerId = User.GetUserId();
    if (managerId == null || !await ManagerCanAccessEmployeeAsync(managerId.Value, companyId, leave.UserId))
        return Forbid();

    if (leave.Status != LeaveStatus.Pending)
        return BadRequest(ApiResponse<string>.FailResponse("Leave already processed"));

    var leaveDays = _leaveCalc.CalculateLeaveDays(leave);

    if ((leave.User!.TotalLeaveBalance - leave.User.UsedLeave) < leaveDays)
        return BadRequest(ApiResponse<string>.FailResponse("Insufficient leave balance"));

    leave.User.UsedLeave += (int)Math.Ceiling(leaveDays);
    leave.Status = LeaveStatus.Approved;

    // 🔥 Notification
    var notification = new Notification
    {
        Id = Guid.NewGuid(),
        UserId = leave.UserId,
        Title = "Leave Approved",
        Message = "Your leave has been approved.",
        IsRead = false
    };

    _context.Notifications.Add(notification);

    // 🔥 EMAIL QUEUE ADD (MAIN FIX)
    _context.EmailQueues.Add(new EmailQueue
    {
        Id = Guid.NewGuid(),
        ToEmail = leave.User.Email,
        Subject = "Leave Approved",
        Body = $"Dear {leave.User.FullName}, your leave from {leave.StartDate:dd MMM} to {leave.EndDate:dd MMM} has been approved.",
        Status = EmailStatus.Pending,
        CreatedAt = DateTime.UtcNow
    });

    await _context.SaveChangesAsync();
    await _audit.LogAsync(managerId.Value, companyId, "APPROVE", "Leave", leave.Id);

    await _hubContext.Clients.User(leave.UserId.ToString())
        .SendAsync("ReceiveNotification", new
        {
            notification.Title,
            notification.Message,
            type = "success"
        });

    await NotifyDataChangedAsync(companyId);
    return Ok(ApiResponse<string>.SuccessResponse("Leave approved successfully"));
}

    // ===================================================
    // 🔹 Manager Reject Leave
    // ===================================================
    [Authorize(Roles = "Manager")]
[HttpPut("reject/{id}")]
public async Task<IActionResult> RejectLeave(Guid id, LeaveAction dto)
{
    var companyId = Guid.Parse(User.FindFirst("CompanyId")!.Value);

    var leave = await _context.Leaves
        .Include(l => l.User)
        .FirstOrDefaultAsync(l => l.Id == id && l.CompanyId == companyId);

    if (leave == null)
        return NotFound(ApiResponse<string>.FailResponse("Leave not found"));

    var managerId = User.GetUserId();
    if (managerId == null || !await ManagerCanAccessEmployeeAsync(managerId.Value, companyId, leave.UserId))
        return Forbid();

    if (leave.Status != LeaveStatus.Pending)
        return BadRequest(ApiResponse<string>.FailResponse("Leave already processed"));

    if (string.IsNullOrWhiteSpace(dto.Comment))
        return BadRequest(ApiResponse<string>.FailResponse("Rejection comment is required"));

    leave.Status = LeaveStatus.Rejected;
    leave.ManagerComment = dto.Comment.Trim();
leave.UpdatedAt = DateTime.UtcNow;

    // 🔥 Notification
    var notification = new Notification
    {
        Id = Guid.NewGuid(),
        UserId = leave.UserId,
        Title = "Leave Rejected",
        Message = "Your leave has been rejected.",
        IsRead = false
    };

    _context.Notifications.Add(notification);

    // 🔥 EMAIL QUEUE ADD
    _context.EmailQueues.Add(new EmailQueue
    {
        Id = Guid.NewGuid(),
        ToEmail = leave.User!.Email,
        Subject = "Leave Rejected",
        Body = $"Dear {leave.User.FullName}, your leave request has been rejected.",
        Status = EmailStatus.Pending,
        CreatedAt = DateTime.UtcNow
    });

    await _context.SaveChangesAsync();
    await _audit.LogAsync(managerId.Value, companyId, "REJECT", "Leave", leave.Id);

    await _hubContext.Clients.User(leave.UserId.ToString())
        .SendAsync("ReceiveNotification", new
        {
            notification.Title,
            notification.Message,
            type = "error"
        });

    await NotifyDataChangedAsync(companyId);
    return Ok(ApiResponse<string>.SuccessResponse("Leave rejected successfully"));
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
        await NotifyDataChangedAsync(companyId);

        return Ok(ApiResponse<string>.SuccessResponse("Leave cancelled successfully"));
    }

    // ===================================================
    // 🔹 Admin Dashboard Summary
    // ===================================================
   [Authorize(Policy = "ViewDashboard")]
[HttpGet("dashboard-summary")]
public async Task<IActionResult> GetDashboardSummary()
{
    var companyId = Guid.Parse(User.FindFirst("CompanyId")!.Value);

    var users = _context.Users.AsNoTracking().Where(u => u.CompanyId == companyId);

    var totalUsers = await users.CountAsync();
    var totalEmployees = await users.CountAsync(u => u.Role == UserRole.Employee);
    var totalManagers = await users.CountAsync(u => u.Role == UserRole.Manager);
    var totalAdmins = await users.CountAsync(u => u.Role == UserRole.Admin);

    var leaves = await _context.Leaves
        .AsNoTracking()
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
        totalUsers,
        totalEmployees,
        totalManagers,
        totalAdmins,
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
// 🔹 Get Leave By Id
// ===================================================
[Authorize]
[HttpGet("{id}")]
public async Task<IActionResult> GetLeaveById(Guid id)
{
    var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    var leave = await _context.Leaves
        .Where(l => l.Id == id && l.UserId == userId)
        .Select(l => new
        {
            l.Id,
            l.StartDate,
            l.EndDate,
            l.Reason,
            l.Status
        })
        .FirstOrDefaultAsync();

    if (leave == null)
        return NotFound("Leave not found");

    return Ok(leave);
}

// ===================================================
// 🔹 Employee Dashboard
// ===================================================
private static readonly string[] DashboardLeaveTypes =
    ["Casual Leave", "Sick Leave", "Earned Leave", "Unpaid Leave"];

private static string NormalizeDashboardLeaveType(string leaveType)
{
    var t = (leaveType ?? string.Empty).Trim().ToLowerInvariant();
    if (t.Contains("casual") || t == "cl") return "Casual Leave";
    if (t.Contains("sick") || t == "sl") return "Sick Leave";
    if (t.Contains("unpaid")) return "Unpaid Leave";
    if (t.Contains("earn") || t.Contains("paid") || t == "el" || t.Contains("annual"))
        return "Earned Leave";
    return (leaveType ?? string.Empty).Trim();
}

[Authorize]
[HttpGet("employee-dashboard")]
public async Task<IActionResult> GetEmployeeDashboard()
{
    var userId = User.GetUserId();
    var companyId = User.GetCompanyId();
    if (userId == null || companyId == null)
        return Unauthorized(ApiResponse<string>.FailResponse("Invalid token"));

    var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId);
    if (user == null)
        return NotFound(ApiResponse<string>.FailResponse("User not found"));

    var allLeaves = await _context.Leaves
        .AsNoTracking()
        .Where(l => l.UserId == userId)
        .OrderByDescending(l => l.CreatedAt)
        .ToListAsync();

    var approvedLeaves = allLeaves.Where(l => l.Status == LeaveStatus.Approved).ToList();

    var policies = await _context.LeavePolicies
        .AsNoTracking()
        .Where(p => p.CompanyId == companyId && p.IsActive)
        .ToListAsync();

    var allocatedByType = DashboardLeaveTypes.ToDictionary(t => t, _ => 0.0);
    foreach (var policy in policies)
    {
        var key = NormalizeDashboardLeaveType(policy.LeaveTypeName);
        if (allocatedByType.ContainsKey(key))
            allocatedByType[key] += policy.MaxDaysPerYear;
    }

    if (policies.Count == 0)
    {
        allocatedByType["Earned Leave"] = user.TotalLeaveBalance;
    }

    var usedByType = DashboardLeaveTypes.ToDictionary(t => t, _ => 0.0);
    foreach (var leave in approvedLeaves)
    {
        var key = NormalizeDashboardLeaveType(leave.LeaveType);
        if (usedByType.ContainsKey(key))
            usedByType[key] += _leaveCalc.CalculateLeaveDays(leave);
    }

    var balanceByType = DashboardLeaveTypes.Select(type => new
    {
        leaveType = type,
        allocated = allocatedByType[type],
        used = usedByType[type],
        remaining = Math.Max(0, allocatedByType[type] - usedByType[type])
    }).ToList();

    var today = DateTime.UtcNow.Date;
    var trendStart = new DateTime(today.Year, today.Month, 1).AddMonths(-5);

    var trendLeaves = approvedLeaves
        .Where(l => l.StartDate >= trendStart)
        .ToList();

    var trendLabels = Enumerable.Range(0, 6)
        .Select(i => trendStart.AddMonths(i))
        .Select(d => d.ToString("MMM"))
        .ToList();

    var usageTrend = DashboardLeaveTypes.Select(type => new
    {
        leaveType = type,
        data = Enumerable.Range(0, 6).Select(monthOffset =>
        {
            var monthStart = trendStart.AddMonths(monthOffset);
            var monthEnd = monthStart.AddMonths(1);
            return trendLeaves
                .Where(l =>
                    NormalizeDashboardLeaveType(l.LeaveType) == type &&
                    l.StartDate >= monthStart &&
                    l.StartDate < monthEnd)
                .Sum(l => _leaveCalc.CalculateLeaveDays(l));
        }).ToList()
    }).ToList();

    var upcomingHolidays = await _context.Holidays
        .AsNoTracking()
        .Where(h => h.CompanyId == companyId && h.IsActive && h.Date >= today)
        .OrderBy(h => h.Date)
        .Take(6)
        .Select(h => new { h.Id, h.Name, h.Date })
        .ToListAsync();

    var teamMemberIds = await _context.Users
        .AsNoTracking()
        .Where(u =>
            u.CompanyId == companyId &&
            u.Id != userId &&
            u.Role == UserRole.Employee &&
            (user.ManagerId == null
                ? u.ManagerId == null
                : u.ManagerId == user.ManagerId))
        .Select(u => u.Id)
        .ToListAsync();

    var windowEnd = today.AddDays(14);
    var teamOnLeave = await _context.Leaves
        .AsNoTracking()
        .Include(l => l.User)
        .Where(l =>
            teamMemberIds.Contains(l.UserId) &&
            (l.Status == LeaveStatus.Approved || l.Status == LeaveStatus.Pending) &&
            l.StartDate <= windowEnd &&
            l.EndDate >= today)
        .OrderBy(l => l.StartDate)
        .Take(8)
        .Select(l => new
        {
            l.Id,
            employee = l.User!.FullName,
            department = l.User.Department,
            l.LeaveType,
            l.StartDate,
            l.EndDate,
            status = l.Status.ToString()
        })
        .ToListAsync();

    var pendingRequests = allLeaves
        .Where(l => l.Status == LeaveStatus.Pending)
        .Take(6)
        .Select(l => new
        {
            l.Id,
            l.LeaveType,
            l.StartDate,
            l.EndDate,
            totalDays = _leaveCalc.CalculateLeaveDays(l),
            l.Reason,
            appliedDate = l.CreatedAt,
            status = l.Status.ToString()
        })
        .ToList();

    var recentLeaves = allLeaves
        .Take(8)
        .Select(l => new
        {
            l.Id,
            l.LeaveType,
            l.StartDate,
            l.EndDate,
            totalDays = _leaveCalc.CalculateLeaveDays(l),
            status = l.Status.ToString(),
            appliedDate = l.CreatedAt
        })
        .ToList();

    return Ok(new
    {
        employeeName = user.FullName,
        summary = new
        {
            pending = allLeaves.Count(l => l.Status == LeaveStatus.Pending),
            approved = allLeaves.Count(l => l.Status == LeaveStatus.Approved),
            rejected = allLeaves.Count(l => l.Status == LeaveStatus.Rejected),
            remaining = Math.Max(0, user.TotalLeaveBalance - user.UsedLeave)
        },
        balanceByType,
        pendingRequests,
        recentLeaves,
        upcomingHolidays,
        teamOnLeave,
        usageTrend = new
        {
            labels = trendLabels,
            series = usageTrend
        }
    });
}
// ===================================================
// 🔹 Edit Leave
// ===================================================
[Authorize]
[HttpPut("edit/{id}")]
public async Task<IActionResult> EditLeave(Guid id, EditLeaveRequest request)
    {var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

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

    var companyId = Guid.Parse(User.FindFirst("CompanyId")!.Value);
    await NotifyDataChangedAsync(companyId);

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
    var companyId = User.GetCompanyId();
    var managerId = User.GetUserId();
    if (companyId == null || managerId == null)
        return Unauthorized();

    var leaves = await _context.Leaves
        .Include(l => l.User)
        .Where(l => l.CompanyId == companyId &&
                    l.Status == LeaveStatus.Pending &&
                    (l.User!.ManagerId == managerId || l.User.ManagerId == null))
        .OrderByDescending(l => l.CreatedAt)
        .ToListAsync();

    var result = leaves.Select(l => new
    {
        l.Id,
        employeeId = l.UserId,
        employee = l.User!.FullName,
        email = l.User.Email,
        department = l.User.Department,
        l.LeaveType,
        priority = l.Priority ?? "Medium",
        l.StartDate,
        l.EndDate,
        totalDays = _leaveCalc.CalculateLeaveDays(l),
        l.Reason,
        appliedDate = l.CreatedAt,
        status = l.Status.ToString(),
        l.IsHalfDay,
        l.HalfDayType
    });

    return Ok(result);
}

[Authorize(Roles = "Manager")]
[HttpGet("approval-detail/{id:guid}")]
public async Task<IActionResult> GetApprovalDetail(Guid id)
{
    var companyId = User.GetCompanyId();
    var managerId = User.GetUserId();
    if (companyId == null || managerId == null)
        return Unauthorized();

    var leave = await _context.Leaves
        .Include(l => l.User)
        .FirstOrDefaultAsync(l => l.Id == id && l.CompanyId == companyId);

    if (leave == null)
        return NotFound(ApiResponse<string>.FailResponse("Leave not found"));

    if (!await ManagerCanAccessEmployeeAsync(managerId.Value, companyId.Value, leave.UserId))
        return Forbid();

    var employee = leave.User!;
    var teamMemberIds = await _context.Users
        .AsNoTracking()
        .Where(u => u.CompanyId == companyId && u.Role == UserRole.Employee &&
                    (u.ManagerId == managerId || u.ManagerId == null))
        .Select(u => u.Id)
        .ToListAsync();

    var policies = await _context.LeavePolicies
        .AsNoTracking()
        .Where(p => p.CompanyId == companyId && p.IsActive)
        .ToListAsync();

    var approvedLeaves = await _context.Leaves
        .AsNoTracking()
        .Where(l => l.UserId == leave.UserId && l.Status == LeaveStatus.Approved)
        .ToListAsync();

    var usedByType = approvedLeaves
        .GroupBy(l => l.LeaveType)
        .ToDictionary(g => g.Key, g => g.Sum(l => _leaveCalc.CalculateLeaveDays(l)));

    var balanceByType = policies.Select(p =>
    {
        usedByType.TryGetValue(p.LeaveTypeName, out var used);
        return new
        {
            leaveType = p.LeaveTypeName,
            allocated = p.MaxDaysPerYear,
            used,
            remaining = Math.Max(0, p.MaxDaysPerYear - used)
        };
    }).ToList();

    if (balanceByType.Count == 0)
    {
        balanceByType.Add(new
        {
            leaveType = "Annual",
            allocated = employee.TotalLeaveBalance,
            used = (double)employee.UsedLeave,
            remaining = (double)Math.Max(0, employee.TotalLeaveBalance - employee.UsedLeave)
        });
    }

    var previousWithDays = await _context.Leaves
        .AsNoTracking()
        .Where(l => l.UserId == leave.UserId && l.Id != leave.Id)
        .OrderByDescending(l => l.CreatedAt)
        .Take(8)
        .ToListAsync();

    var overlapping = await _context.Leaves
        .AsNoTracking()
        .Include(l => l.User)
        .Where(l => l.CompanyId == companyId &&
                    l.Id != leave.Id &&
                    teamMemberIds.Contains(l.UserId) &&
                    (l.Status == LeaveStatus.Approved || l.Status == LeaveStatus.Pending) &&
                    l.StartDate <= leave.EndDate &&
                    l.EndDate >= leave.StartDate)
        .Select(l => new
        {
            l.Id,
            employee = l.User!.FullName,
            l.LeaveType,
            l.StartDate,
            l.EndDate,
            status = l.Status.ToString()
        })
        .ToListAsync();

    var auditTrail = await _context.AuditLogs
        .AsNoTracking()
        .Where(a => a.CompanyId == companyId &&
                    a.EntityName == "Leave" &&
                    (a.EntityId == leave.Id || a.EntityId == leave.UserId))
        .OrderByDescending(a => a.CreatedAt)
        .Take(15)
        .Join(
            _context.Users.AsNoTracking(),
            a => a.UserId,
            u => u.Id,
            (a, u) => new
            {
                a.Action,
                performedBy = u.FullName,
                a.CreatedAt
            })
        .ToListAsync();

    return Ok(new
    {
        leave = new
        {
            leave.Id,
            leave.LeaveType,
            leave.StartDate,
            leave.EndDate,
            totalDays = _leaveCalc.CalculateLeaveDays(leave),
            leave.Reason,
            leave.Status,
            appliedDate = leave.CreatedAt,
            leave.IsHalfDay,
            leave.HalfDayType,
            leave.Priority,
            leave.ManagerComment
        },
        employee = new
        {
            employee.Id,
            employee.FullName,
            employee.Email,
            employee.Department,
            managerName = await _context.Users
                .Where(m => m.Id == employee.ManagerId)
                .Select(m => m.FullName)
                .FirstOrDefaultAsync()
        },
        balanceByType,
        previousLeaves = previousWithDays.Select(l => new
        {
            l.Id,
            l.LeaveType,
            l.StartDate,
            l.EndDate,
            status = l.Status.ToString(),
            totalDays = _leaveCalc.CalculateLeaveDays(l)
        }),
        overlappingTeamLeaves = overlapping,
        auditTrail
    });
}

[Authorize(Roles = "Manager")]
[HttpPost("bulk-approve")]
public async Task<IActionResult> BulkApprove([FromBody] BulkLeaveActionRequest request)
{
    if (request.LeaveIds == null || request.LeaveIds.Count == 0)
        return BadRequest(ApiResponse<string>.FailResponse("No leave requests selected"));

    var approved = 0;
    var failed = new List<string>();

    foreach (var leaveId in request.LeaveIds.Distinct())
    {
        var result = await ApproveLeaveInternal(leaveId, request.Comment);
        if (result.Success)
            approved++;
        else
            failed.Add($"{leaveId}: {result.Message}");
    }

    if (Guid.TryParse(User.FindFirst("CompanyId")?.Value, out var bulkCompanyId))
        await NotifyDataChangedAsync(bulkCompanyId);

    return Ok(ApiResponse<object>.SuccessResponse(new { approved, failed }, $"Processed {approved} leave(s)"));
}

[Authorize(Roles = "Manager")]
[HttpPost("bulk-reject")]
public async Task<IActionResult> BulkReject([FromBody] BulkLeaveActionRequest request)
{
    if (string.IsNullOrWhiteSpace(request.Comment))
        return BadRequest(ApiResponse<string>.FailResponse("Rejection comment is required"));

    if (request.LeaveIds == null || request.LeaveIds.Count == 0)
        return BadRequest(ApiResponse<string>.FailResponse("No leave requests selected"));

    var rejected = 0;
    var failed = new List<string>();

    foreach (var leaveId in request.LeaveIds.Distinct())
    {
        var result = await RejectLeaveInternal(leaveId, request.Comment!.Trim());
        if (result.Success)
            rejected++;
        else
            failed.Add($"{leaveId}: {result.Message}");
    }

    if (Guid.TryParse(User.FindFirst("CompanyId")?.Value, out var bulkCompanyId))
        await NotifyDataChangedAsync(bulkCompanyId);

    return Ok(ApiResponse<object>.SuccessResponse(new { rejected, failed }, $"Rejected {rejected} leave(s)"));
}

private async Task<(bool Success, string Message)> ApproveLeaveInternal(Guid id, string? comment)
{
    var companyIdClaim = User.FindFirst("CompanyId")?.Value;
    if (!Guid.TryParse(companyIdClaim, out var companyId))
        return (false, "Invalid company");

    var leave = await _context.Leaves
        .Include(l => l.User)
        .FirstOrDefaultAsync(l => l.Id == id && l.CompanyId == companyId);

    if (leave == null) return (false, "Not found");

    var managerId = User.GetUserId();
    if (managerId == null || !await ManagerCanAccessEmployeeAsync(managerId.Value, companyId, leave.UserId))
        return (false, "Forbidden");

    if (leave.Status != LeaveStatus.Pending)
        return (false, "Already processed");

    var leaveDays = _leaveCalc.CalculateLeaveDays(leave);
    if ((leave.User!.TotalLeaveBalance - leave.User.UsedLeave) < leaveDays)
        return (false, "Insufficient balance");

    leave.User.UsedLeave += (int)Math.Ceiling(leaveDays);
    leave.Status = LeaveStatus.Approved;
    if (!string.IsNullOrWhiteSpace(comment))
        leave.ManagerComment = comment.Trim();
    leave.UpdatedAt = DateTime.UtcNow;

    _context.Notifications.Add(new Notification
    {
        Id = Guid.NewGuid(),
        UserId = leave.UserId,
        Title = "Leave Approved",
        Message = "Your leave has been approved.",
        IsRead = false
    });

    _context.EmailQueues.Add(new EmailQueue
    {
        Id = Guid.NewGuid(),
        ToEmail = leave.User.Email,
        Subject = "Leave Approved",
        Body = $"Dear {leave.User.FullName}, your leave from {leave.StartDate:dd MMM} to {leave.EndDate:dd MMM} has been approved.",
        Status = EmailStatus.Pending,
        CreatedAt = DateTime.UtcNow
    });

    await _context.SaveChangesAsync();
    await _audit.LogAsync(managerId.Value, companyId, "APPROVE", "Leave", leave.Id);

    await _hubContext.Clients.User(leave.UserId.ToString())
        .SendAsync("ReceiveNotification", new
        {
            Title = "Leave Approved",
            Message = "Your leave has been approved.",
            type = "success"
        });

    await NotifyDataChangedAsync(companyId);
    return (true, "OK");
}

private async Task<(bool Success, string Message)> RejectLeaveInternal(Guid id, string comment)
{
    var companyId = Guid.Parse(User.FindFirst("CompanyId")!.Value);

    var leave = await _context.Leaves
        .Include(l => l.User)
        .FirstOrDefaultAsync(l => l.Id == id && l.CompanyId == companyId);

    if (leave == null) return (false, "Not found");

    var managerId = User.GetUserId();
    if (managerId == null || !await ManagerCanAccessEmployeeAsync(managerId.Value, companyId, leave.UserId))
        return (false, "Forbidden");

    if (leave.Status != LeaveStatus.Pending)
        return (false, "Already processed");

    leave.Status = LeaveStatus.Rejected;
    leave.ManagerComment = comment;
    leave.UpdatedAt = DateTime.UtcNow;

    _context.Notifications.Add(new Notification
    {
        Id = Guid.NewGuid(),
        UserId = leave.UserId,
        Title = "Leave Rejected",
        Message = "Your leave has been rejected.",
        IsRead = false
    });

    _context.EmailQueues.Add(new EmailQueue
    {
        Id = Guid.NewGuid(),
        ToEmail = leave.User!.Email,
        Subject = "Leave Rejected",
        Body = $"Dear {leave.User.FullName}, your leave request has been rejected.",
        Status = EmailStatus.Pending,
        CreatedAt = DateTime.UtcNow
    });

    await _context.SaveChangesAsync();
    await _audit.LogAsync(managerId.Value, companyId, "REJECT", "Leave", leave.Id);

    await _hubContext.Clients.User(leave.UserId.ToString())
        .SendAsync("ReceiveNotification", new
        {
            Title = "Leave Rejected",
            Message = "Your leave has been rejected.",
            type = "error"
        });

    await NotifyDataChangedAsync(companyId);
    return (true, "OK");
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

[Authorize]
[HttpGet("leave-calendar")]
public async Task<IActionResult> GetLeaveCalendar()
{
    var companyId = Guid.Parse(User.FindFirst("CompanyId")!.Value);

    var leaves = await _context.Leaves
        .Include(l => l.User)
        .Where(l => l.CompanyId == companyId && l.Status == LeaveStatus.Approved)
        .Select(l => new
        {
            employee = l.User!.FullName,
            department = l.User.Department,
            startDate = l.StartDate,
            endDate = l.EndDate,
            leaveType = l.LeaveType
        })
        .ToListAsync();

    return Ok(leaves);
}
}