using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Security.Claims;
using System.Text.Json;
using lms_api.Data;
using lms_api.Models.Enums;

namespace lms_api.Controllers;

[ApiController]
[Route("api/[controller]")]
// [Authorize(Policy = "ViewReports")]
public class ReportController : ControllerBase
{
    private readonly AppDbContext _context;
 

  public ReportController(AppDbContext context)
{
    _context = context;
}

    private Guid CompanyId =>
        Guid.Parse(User.FindFirst("CompanyId")!.Value);

   

    // ============================================================
    // 1️⃣ Date Range Leave Summary
    // ============================================================
    [HttpGet("leave-summary")]
    public async Task<IActionResult> GetLeaveSummary(DateTime start, DateTime end)
    {
        var summary = await _context.Leaves
            .AsNoTracking()
            .Where(l => l.CompanyId == CompanyId &&
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
    var data = await _context.Leaves
        .AsNoTracking()
        .Where(l => l.CompanyId == CompanyId &&
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
        var data = await _context.Leaves
            .AsNoTracking()
            .Where(l => l.CompanyId == CompanyId)
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
        var data = await _context.Leaves
            .AsNoTracking()
            .Where(l => l.CompanyId == CompanyId)
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
        var data = await _context.Leaves
            .AsNoTracking()
            .Where(l => l.CompanyId == CompanyId)
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

    // ============================================================
    // 6️⃣ CSV Export
    // ============================================================
    [HttpGet("export-csv")]
    public async Task<IActionResult> ExportCsv()
    {
        var leaves = await _context.Leaves
            .Include(l => l.User)
            .Where(l => l.CompanyId == CompanyId)
            .ToListAsync();

        var csv = new StringBuilder();
        csv.AppendLine("Employee,Department,StartDate,EndDate,Status,Reason");

        foreach (var l in leaves)
        {
            csv.AppendLine(
                $"{l.User?.FullName ?? "Unknown"}," +
                $"{l.User?.Department ?? "General"}," +
                $"{l.StartDate:yyyy-MM-dd}," +
                $"{l.EndDate:yyyy-MM-dd}," +
                $"{l.Status}," +
                $"{l.Reason}"
            );
        }

        return File(
            Encoding.UTF8.GetBytes(csv.ToString()),
            "text/csv",
            "leave-report.csv"
        );
    }

    // ============================================================
    // 7️⃣ Unified Dashboard Analytics (Redis Cached)
    // ============================================================
  [HttpGet("dashboard-analytics")]
public async Task<IActionResult> GetDashboardAnalytics(int year)
{
    var companyIdClaim = User.FindFirst("CompanyId")?.Value;

    if (!Guid.TryParse(companyIdClaim, out var companyId))
        return Unauthorized("Invalid company token");

    var totalEmployees = await _context.Users
        .CountAsync(u => u.CompanyId == companyId);

    var totalLeaves = await _context.Leaves
        .CountAsync(l => l.CompanyId == companyId);

    var approved = await _context.Leaves
        .CountAsync(l => l.CompanyId == companyId &&
                         l.Status == LeaveStatus.Approved);

    var pending = await _context.Leaves
        .CountAsync(l => l.CompanyId == companyId &&
                         l.Status == LeaveStatus.Pending);

    var rejected = await _context.Leaves
        .CountAsync(l => l.CompanyId == companyId &&
                         l.Status == LeaveStatus.Rejected);

    var monthlyTrends = await _context.Leaves
        .Where(l => l.CompanyId == companyId && l.StartDate.Year == year)
        .GroupBy(l => l.StartDate.Month)
        .Select(g => new
        {
            month = g.Key,
            total = g.Count()
        })
        .OrderBy(x => x.month)
        .ToListAsync();

    var leaveTypes = await _context.Leaves
        .Where(l => l.CompanyId == companyId)
        .GroupBy(l => l.LeaveType)
        .Select(g => new
        {
            type = g.Key.ToString(),
            count = g.Count()
        })
        .ToListAsync();

    var result = new
    {
        totalEmployees,
        totalLeaves,
        pendingLeaves = pending,
        approvedLeaves = approved,

        months = monthlyTrends.Select(x => x.month),
        monthlyLeaves = monthlyTrends.Select(x => x.total),

        casualLeaves = leaveTypes.FirstOrDefault(x => x.type == "Casual")?.count ?? 0,
        sickLeaves = leaveTypes.FirstOrDefault(x => x.type == "Sick")?.count ?? 0,
        earnedLeaves = leaveTypes.FirstOrDefault(x => x.type == "Earned")?.count ?? 0
    };

    return Ok(result);
}

    // ============================================================
    // 8️⃣ Trend Comparison
    // ============================================================
    [HttpGet("trend-comparison")]
    public async Task<IActionResult> GetTrendComparison(int year)
    {
        var currentYearTotal = await _context.Leaves
            .CountAsync(l => l.CompanyId == CompanyId &&
                             l.StartDate.Year == year);

        var previousYearTotal = await _context.Leaves
            .CountAsync(l => l.CompanyId == CompanyId &&
                             l.StartDate.Year == year - 1);

        double growthPercentage = 0;

        if (previousYearTotal > 0)
        {
            growthPercentage =
                ((double)(currentYearTotal - previousYearTotal)
                / previousYearTotal) * 100;
        }

        return Ok(new
        {
            currentYear = year,
            currentTotal = currentYearTotal,
            previousYear = year - 1,
            previousTotal = previousYearTotal,
            growthPercentage = Math.Round(growthPercentage, 2)
        });
    }
}