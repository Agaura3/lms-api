using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using lms_api.Common;
using lms_api.Data;
using lms_api.DTOs;
using lms_api.Extensions;
using lms_api.Models;
using lms_api.Services;

namespace lms_api.Controllers;

[ApiController]
[Route("api/settings")]
[Authorize(Roles = "Admin")]
public class SettingsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IAuditService _audit;

    public SettingsController(AppDbContext context, IAuditService audit)
    {
        _context = context;
        _audit = audit;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var companyId = User.GetCompanyId();
        if (companyId == null) return Unauthorized();

        var settings = await _context.CompanySettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.CompanyId == companyId);

        if (settings == null)
        {
            settings = new CompanySettings { CompanyId = companyId.Value };
            _context.CompanySettings.Add(settings);
            await _context.SaveChangesAsync();
        }

        return Ok(ApiResponse<object>.SuccessResponse(new
        {
            settings.DefaultAnnualLeaveDays,
            settings.TimeZone,
            settings.DateFormat,
            settings.EmailNotificationsEnabled
        }));
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] UpdateCompanySettingsRequest request)
    {
        var companyId = User.GetCompanyId();
        var userId = User.GetUserId();
        if (companyId == null || userId == null) return Unauthorized();

        var settings = await _context.CompanySettings
            .FirstOrDefaultAsync(s => s.CompanyId == companyId);

        if (settings == null)
        {
            settings = new CompanySettings { CompanyId = companyId.Value };
            _context.CompanySettings.Add(settings);
        }

        settings.DefaultAnnualLeaveDays = request.DefaultAnnualLeaveDays;
        settings.TimeZone = request.TimeZone;
        settings.DateFormat = request.DateFormat;
        settings.EmailNotificationsEnabled = request.EmailNotificationsEnabled;
        settings.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        await _audit.LogAsync(userId.Value, companyId.Value, "UPDATE", "CompanySettings", settings.Id);

        return Ok(ApiResponse<string>.SuccessResponse("Settings updated"));
    }
}
