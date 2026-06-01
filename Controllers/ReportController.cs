using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Security.Claims;
using System.Text.Json;
using lms_api.Data;
using lms_api.Models.Enums;
using lms_api.Common;
using System.Linq;

namespace lms_api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class ReportController : ControllerBase
{
    private readonly AppDbContext _context;

    public ReportController(AppDbContext context)
    {
        _context = context;
    }

    private sealed class LeaveReportRow
    {
        public LeaveStatus Status { get; init; }
        public string LeaveType { get; init; } = string.Empty;
        public DateTime StartDate { get; init; }
        public DateTime EndDate { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime? UpdatedAt { get; init; }
        public string Department { get; init; } = "Unassigned";
        public string FullName { get; init; } = "Unknown";
    }

    private bool TryGetCompanyId(out Guid companyId)
    {
        var claimValue = User.FindFirst("CompanyId")?.Value
            ?? User.Claims.FirstOrDefault(c =>
                c.Type.Contains("CompanyId", StringComparison.OrdinalIgnoreCase))?.Value;

        if (string.IsNullOrWhiteSpace(claimValue) || !Guid.TryParse(claimValue, out companyId))
        {
            companyId = default;
            return false;
        }

        return true;
    }

    private IActionResult? RequireCompanyId(out Guid companyId)
    {
        if (TryGetCompanyId(out companyId))
            return null;
        return Unauthorized(new { message = "CompanyId missing from token." });
    }

   

    // ============================================================
    // 1️⃣ Date Range Leave Summary
    // ============================================================
    [HttpGet("leave-summary")]
    public async Task<IActionResult> GetLeaveSummary(DateTime start, DateTime end)
    {
        if (RequireCompanyId(out var companyId) is { } authError)
            return authError;

        start = DateTimeUtil.ToUtcStartOfDay(start);
        end = DateTimeUtil.ToUtcEndOfDay(end);

        var summary = await _context.Leaves
            .AsNoTracking()
            .Where(l => l.CompanyId == companyId &&
                        l.StartDate >= start &&
                        l.EndDate <= end)
            .GroupBy(l => l.Status)
            .Select(g => new
            {
                Status = g.Key.ToString(),
                Count = g.Count()
            })
            .ToListAsync();

        return Ok(summary);
    }

    // ============================================================
    // 2️⃣ Monthly Trends (Redis Cached)
    // ============================================================
   [HttpGet("monthly-trends")]
public async Task<IActionResult> GetMonthlyTrends(int year)
{
    if (RequireCompanyId(out var companyId) is { } authError)
        return authError;

    var data = await _context.Leaves
        .AsNoTracking()
        .Where(l => l.CompanyId == companyId &&
                    l.StartDate.Year == year)
        .GroupBy(l => l.StartDate.Month)
        .Select(g => new
        {
            Month = g.Key,
            TotalLeaves = g.Count(),
            Approved = g.Count(x => x.Status == LeaveStatus.Approved),
            Pending = g.Count(x => x.Status == LeaveStatus.Pending),
            Rejected = g.Count(x => x.Status == LeaveStatus.Rejected)
        })
        .OrderBy(x => x.Month)
        .ToListAsync();

    return Ok(data);
}

    // ============================================================
    // 3️⃣ Employee-wise Breakdown
    // ============================================================
    [HttpGet("employee-breakdown")]
    public async Task<IActionResult> GetEmployeeBreakdown()
    {
        if (RequireCompanyId(out var companyId) is { } authError)
            return authError;

        var data = await _context.Leaves
            .AsNoTracking()
            .Where(l => l.CompanyId == companyId)
            .Select(l => new
            {
                EmployeeName = l.User!.FullName,
                l.Status
            })
            .GroupBy(x => x.EmployeeName)
            .Select(g => new
            {
                Employee = g.Key,
                TotalLeaves = g.Count(),
                Approved = g.Count(x => x.Status == LeaveStatus.Approved),
                Pending = g.Count(x => x.Status == LeaveStatus.Pending),
                Rejected = g.Count(x => x.Status == LeaveStatus.Rejected)
            })
            .OrderByDescending(x => x.TotalLeaves)
            .ToListAsync();

        return Ok(data);
    }

    // ============================================================
    // 4️⃣ Leave Type Analytics
    // ============================================================
    [HttpGet("leave-type-analysis")]
    public async Task<IActionResult> GetLeaveTypeAnalysis()
    {
        if (RequireCompanyId(out var companyId) is { } authError)
            return authError;

        var data = await _context.Leaves
            .AsNoTracking()
            .Where(l => l.CompanyId == companyId)
            .GroupBy(l => l.LeaveType)
            .Select(g => new
            {
                LeaveType = g.Key.ToString(),
                Total = g.Count(),
                Approved = g.Count(x => x.Status == LeaveStatus.Approved),
                Pending = g.Count(x => x.Status == LeaveStatus.Pending),
                Rejected = g.Count(x => x.Status == LeaveStatus.Rejected)
            })
            .OrderByDescending(x => x.Total)
            .ToListAsync();

        return Ok(data);
    }

    // ============================================================
    // 5️⃣ Department-wise Analytics
    // ============================================================
    [HttpGet("department-analysis")]
    public async Task<IActionResult> GetDepartmentAnalysis()
    {
        if (RequireCompanyId(out var companyId) is { } authError)
            return authError;

        var data = await _context.Leaves
            .AsNoTracking()
            .Where(l => l.CompanyId == companyId)
            .Select(l => new
            {
                Department = l.User!.Department,
                l.Status
            })
            .GroupBy(x => x.Department)
            .Select(g => new
            {
                Department = g.Key,
                TotalLeaves = g.Count(),
                Approved = g.Count(x => x.Status == LeaveStatus.Approved),
                Pending = g.Count(x => x.Status == LeaveStatus.Pending),
                Rejected = g.Count(x => x.Status == LeaveStatus.Rejected)
            })
            .OrderByDescending(x => x.TotalLeaves)
            .ToListAsync();

        return Ok(data);
    }

    private IQueryable<Models.Leave> FilteredLeavesQuery(
        Guid companyId,
        DateTime start,
        DateTime end,
        string? department,
        string? leaveType)
    {
        var query = _context.Leaves
            .AsNoTracking()
            .Include(l => l.User)
            .Where(l => l.CompanyId == companyId &&
                        l.StartDate <= end &&
                        l.EndDate >= start);

        if (!string.IsNullOrWhiteSpace(department))
            query = query.Where(l => l.User != null && l.User.Department == department);

        if (!string.IsNullOrWhiteSpace(leaveType))
            query = query.Where(l => l.LeaveType == leaveType);

        return query;
    }

    private async Task<List<LeaveReportRow>> FetchLeaveReportRowsAsync(
        Guid companyId,
        DateTime start,
        DateTime end,
        string? department,
        string? leaveType)
    {
        var query = _context.Leaves
            .AsNoTracking()
            .Where(l => l.CompanyId == companyId &&
                        l.StartDate <= end &&
                        l.EndDate >= start);

        if (!string.IsNullOrWhiteSpace(department))
            query = query.Where(l => l.User != null && l.User.Department == department);

        if (!string.IsNullOrWhiteSpace(leaveType))
            query = query.Where(l => l.LeaveType == leaveType);

        return await query
            .Select(l => new LeaveReportRow
            {
                Status = l.Status,
                LeaveType = l.LeaveType ?? string.Empty,
                StartDate = l.StartDate,
                EndDate = l.EndDate,
                CreatedAt = l.CreatedAt,
                UpdatedAt = l.UpdatedAt,
                Department = l.User != null ? l.User.Department : "Unassigned",
                FullName = l.User != null ? l.User.FullName : "Unknown"
            })
            .ToListAsync();
    }

    // ============================================================
    // 6️⃣ CSV Export (filtered)
    // ============================================================
    [HttpGet("export-csv")]
    public async Task<IActionResult> ExportCsv(
        DateTime? start,
        DateTime? end,
        string? department,
        string? leaveType)
    {
        if (RequireCompanyId(out var companyId) is { } authError)
            return authError;

        var (startDate, endDate) = DateTimeUtil.NormalizeReportRange(start, end);

        var leaves = await FilteredLeavesQuery(companyId, startDate, endDate, department, leaveType)
            .OrderByDescending(l => l.StartDate)
            .ToListAsync();

        var csv = new StringBuilder();
        csv.AppendLine("Employee,Department,LeaveType,StartDate,EndDate,Status,Days,AppliedAt,ResolvedAt,Reason");

        foreach (var l in leaves)
        {
            var days = (l.EndDate.Date - l.StartDate.Date).Days + 1;
            csv.AppendLine(
                $"{EscapeCsv(l.User?.FullName ?? "Unknown")}," +
                $"{EscapeCsv(l.User?.Department ?? "General")}," +
                $"{EscapeCsv(l.LeaveType)}," +
                $"{l.StartDate:yyyy-MM-dd}," +
                $"{l.EndDate:yyyy-MM-dd}," +
                $"{l.Status}," +
                $"{days}," +
                $"{l.CreatedAt:yyyy-MM-dd HH:mm}," +
                $"{(l.UpdatedAt.HasValue ? l.UpdatedAt.Value.ToString("yyyy-MM-dd HH:mm") : "")}," +
                $"{EscapeCsv(l.Reason)}"
            );
        }

        return File(
            Encoding.UTF8.GetBytes(csv.ToString()),
            "text/csv",
            $"hr-leave-report-{startDate:yyyyMMdd}-{endDate:yyyyMMdd}.csv"
        );
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    // ============================================================
    // 8️⃣ HR Analytics Dashboard (unified, filterable)
    // ============================================================
    [HttpGet("hr-analytics")]
    public async Task<IActionResult> GetHrAnalytics(
        DateTime? start,
        DateTime? end,
        string? department,
        string? leaveType)
    {
        if (RequireCompanyId(out var companyId) is { } authError)
            return authError;

        var (startDate, endDate) = DateTimeUtil.NormalizeReportRange(start, end);
        if (endDate < startDate)
            return BadRequest(new { message = "End date must be on or after start date." });

        try
        {
            var leaves = await FetchLeaveReportRowsAsync(companyId, startDate, endDate, department, leaveType);

        var totalEmployees = await _context.Users
            .AsNoTracking()
            .Where(u => u.CompanyId == companyId)
            .CountAsync();

        var departments = await _context.Users
            .AsNoTracking()
            .Where(u => u.CompanyId == companyId && u.Department != null && u.Department != "")
            .Select(u => u.Department!)
            .Distinct()
            .OrderBy(d => d)
            .ToListAsync();

        var leaveTypes = leaves
            .Select(l => l.LeaveType)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        var statusCounts = leaves
            .GroupBy(l => l.Status)
            .ToDictionary(g => g.Key.ToString(), g => g.Count());

        int CountStatus(LeaveStatus status) =>
            statusCounts.TryGetValue(status.ToString(), out var c) ? c : 0;

        var approvedLeaves = leaves.Where(l => l.Status == LeaveStatus.Approved).ToList();
        var avgApprovalHours = approvedLeaves
            .Where(l => l.UpdatedAt.HasValue)
            .Select(l => (l.UpdatedAt!.Value - l.CreatedAt).TotalHours)
            .DefaultIfEmpty(0)
            .Average();

        var monthlyTrends = leaves
            .GroupBy(l => l.StartDate.Month)
            .Select(g => new
            {
                month = g.Key,
                monthLabel = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedMonthName(g.Key),
                total = g.Count(),
                approved = g.Count(x => x.Status == LeaveStatus.Approved),
                pending = g.Count(x => x.Status == LeaveStatus.Pending),
                rejected = g.Count(x => x.Status == LeaveStatus.Rejected)
            })
            .OrderBy(x => x.month)
            .ToList();

        var departmentTrends = leaves
            .GroupBy(l => l.Department)
            .Select(g => new
            {
                department = g.Key,
                total = g.Count(),
                approved = g.Count(x => x.Status == LeaveStatus.Approved),
                pending = g.Count(x => x.Status == LeaveStatus.Pending),
                rejected = g.Count(x => x.Status == LeaveStatus.Rejected)
            })
            .OrderByDescending(x => x.total)
            .ToList();

        var leaveTypeDistribution = leaves
            .GroupBy(l => string.IsNullOrWhiteSpace(l.LeaveType) ? "Other" : l.LeaveType)
            .Select(g => new { leaveType = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count)
            .ToList();

        var employeeDistribution = leaves
            .GroupBy(l => l.FullName)
            .Select(g => new
            {
                employee = g.Key,
                leaveCount = g.Count(),
                totalDays = g.Sum(l => (l.EndDate.Date - l.StartDate.Date).Days + 1)
            })
            .OrderByDescending(x => x.totalDays)
            .Take(15)
            .ToList();

        var heatmap = BuildAbsenteeHeatmap(approvedLeaves, startDate, endDate);

        var approvalTurnaround = approvedLeaves
            .Where(l => l.UpdatedAt.HasValue)
            .GroupBy(l => l.UpdatedAt!.Value.Month)
            .Select(g =>
            {
                var hours = g.Select(l => (l.UpdatedAt!.Value - l.CreatedAt).TotalHours).ToList();
                hours.Sort();
                return new
                {
                    month = g.Key,
                    monthLabel = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedMonthName(g.Key),
                    avgHours = Math.Round(hours.Average(), 1),
                    medianHours = Math.Round(Percentile(hours, 0.5), 1),
                    p90Hours = Math.Round(Percentile(hours, 0.9), 1),
                    count = hours.Count
                };
            })
            .OrderBy(x => x.month)
            .ToList();

        return Ok(new
        {
            filters = new { start = startDate, end = endDate, department, leaveType },
            departments,
            leaveTypes,
            kpis = new
            {
                totalEmployees,
                totalLeaves = leaves.Count,
                pendingLeaves = CountStatus(LeaveStatus.Pending),
                approvedLeaves = CountStatus(LeaveStatus.Approved),
                rejectedLeaves = CountStatus(LeaveStatus.Rejected),
                avgApprovalHours = Math.Round(avgApprovalHours, 1)
            },
            monthlyTrends,
            departmentTrends,
            leaveTypeDistribution,
            employeeDistribution,
            absenteeHeatmap = heatmap,
            approvalTurnaround
        });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Failed to build HR analytics.", detail = ex.Message });
        }
    }

    private static double Percentile(List<double> sorted, double percentile)
    {
        if (sorted.Count == 0) return 0;
        if (sorted.Count == 1) return sorted[0];
        var index = (sorted.Count - 1) * percentile;
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);
        if (lower == upper) return sorted[lower];
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (index - lower);
    }

    private static object BuildAbsenteeHeatmap(
        List<LeaveReportRow> approvedLeaves,
        DateTime startDate,
        DateTime endDate)
    {
        var dayLabels = new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
        var counts = new int[7, 5];

        foreach (var leave in approvedLeaves)
        {
            var from = leave.StartDate.Date < startDate.Date ? startDate.Date : leave.StartDate.Date;
            var to = leave.EndDate.Date > endDate.Date ? endDate.Date : leave.EndDate.Date;

            for (var day = from; day <= to; day = day.AddDays(1))
            {
                var dow = ((int)day.DayOfWeek + 6) % 7;
                var weekIndex = Math.Min((day.Day - 1) / 7, 4);
                counts[dow, weekIndex]++;
            }
        }

        var cells = new List<object>();
        var max = 0;
        for (var w = 0; w < 5; w++)
        for (var d = 0; d < 7; d++)
            max = Math.Max(max, counts[d, w]);

        for (var w = 0; w < 5; w++)
        for (var d = 0; d < 7; d++)
        {
            cells.Add(new
            {
                dayOfWeek = d,
                dayLabel = dayLabels[d],
                weekIndex = w,
                weekLabel = $"W{w + 1}",
                count = counts[d, w],
                intensity = max == 0 ? 0 : Math.Round((double)counts[d, w] / max, 2)
            });
        }

        return new
        {
            dayLabels,
            weekLabels = new[] { "W1", "W2", "W3", "W4", "W5" },
            maxCount = max,
            cells
        };
    }

    // ============================================================
    // 7️⃣ Unified Dashboard Analytics (Redis Cached)
    // ============================================================
  [HttpGet("dashboard-analytics")]
public async Task<IActionResult> GetDashboardAnalytics(int year)
{
        var companyIdClaim = User.FindFirst("CompanyId")?.Value;

        if (string.IsNullOrEmpty(companyIdClaim))
            return Unauthorized("CompanyId missing");

        if (!Guid.TryParse(companyIdClaim, out var companyId))
            return Unauthorized("Invalid company token");

        var start = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
var end = new DateTime(year + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // KPI Counts
        var totalEmployees = await _context.Users
            .Where(u => u.CompanyId == companyId)
            .CountAsync();

        var totalLeaves = await _context.Leaves
            .Where(l => l.CompanyId == companyId)
            .CountAsync();

        var approved = await _context.Leaves
            .Where(l => l.CompanyId == companyId && l.Status == LeaveStatus.Approved)
            .CountAsync();

        var pending = await _context.Leaves
            .Where(l => l.CompanyId == companyId && l.Status == LeaveStatus.Pending)
            .CountAsync();

        var rejected = await _context.Leaves
            .Where(l => l.CompanyId == companyId && l.Status == LeaveStatus.Rejected)
            .CountAsync();

        // Monthly trends
        var monthlyTrends = await _context.Leaves
            .Where(l => l.CompanyId == companyId &&
                        l.StartDate >= start &&
                        l.StartDate < end)
            .GroupBy(l => l.StartDate.Month)
            .Select(g => new
            {
                month = g.Key,
                total = g.Count()
            })
            .OrderBy(x => x.month)
            .ToListAsync();

        // Leave type distribution
        var leaveTypes = await _context.Leaves
            .Where(l => l.CompanyId == companyId)
            .GroupBy(l => l.LeaveType)
            .Select(g => new
            {
                type = g.Key,
                count = g.Count()
            })
            .ToListAsync();

        var departmentBreakdown = await _context.Leaves
            .AsNoTracking()
            .Where(l => l.CompanyId == companyId &&
                        l.StartDate >= start &&
                        l.StartDate < end)
            .Select(l => new { Department = l.User!.Department ?? "Unassigned" })
            .GroupBy(x => x.Department)
            .Select(g => new
            {
                department = g.Key,
                total = g.Count()
            })
            .OrderByDescending(x => x.total)
            .ToListAsync();

        // Convert month numbers → names
        var months = monthlyTrends
            .Select(x => System.Globalization.CultureInfo
                .CurrentCulture
                .DateTimeFormat
                .GetAbbreviatedMonthName(x.month));

        var result = new
        {
            totalEmployees,
            totalLeaves,
            pendingLeaves = pending,
            approvedLeaves = approved,
            rejectedLeaves = rejected,

            months,
            monthlyLeaves = monthlyTrends.Select(x => x.total),

            casualLeaves = leaveTypes.FirstOrDefault(x => x.type == "Casual")?.count ?? 0,
            sickLeaves = leaveTypes.FirstOrDefault(x => x.type == "Sick")?.count ?? 0,
            earnedLeaves = leaveTypes.FirstOrDefault(x => x.type == "Earned")?.count ?? 0,

            departmentLabels = departmentBreakdown.Select(x => x.department).ToList(),
            departmentTotals = departmentBreakdown.Select(x => x.total).ToList()
        };

        return Ok(result);
}
}