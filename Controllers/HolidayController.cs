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
[Route("api/holidays")]
[Authorize(Roles = "Admin")]
public class HolidayController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IAuditService _audit;

    public HolidayController(AppDbContext context, IAuditService audit)
    {
        _context = context;
        _audit = audit;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var companyId = User.GetCompanyId();
        if (companyId == null) return Unauthorized();

        var holidays = await _context.Holidays
            .AsNoTracking()
            .Where(h => h.CompanyId == companyId && h.IsActive)
            .OrderBy(h => h.Date)
            .Select(h => new { h.Id, h.Name, h.Date })
            .ToListAsync();

        return Ok(ApiResponse<object>.SuccessResponse(holidays));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateHolidayRequest request)
    {
        var companyId = User.GetCompanyId();
        var userId = User.GetUserId();
        if (companyId == null || userId == null) return Unauthorized();

        var holiday = new Holiday
        {
            CompanyId = companyId.Value,
            Name = request.Name.Trim(),
            Date = request.Date.Date
        };

        _context.Holidays.Add(holiday);
        await _context.SaveChangesAsync();
        await _audit.LogAsync(userId.Value, companyId.Value, "CREATE", "Holiday", holiday.Id);

        return Ok(ApiResponse<object>.SuccessResponse(new { holiday.Id, holiday.Name, holiday.Date }, "Holiday created"));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateHolidayRequest request)
    {
        var companyId = User.GetCompanyId();
        var userId = User.GetUserId();
        if (companyId == null || userId == null) return Unauthorized();

        var holiday = await _context.Holidays
            .FirstOrDefaultAsync(h => h.Id == id && h.CompanyId == companyId);

        if (holiday == null) return NotFound(ApiResponse<string>.FailResponse("Holiday not found"));

        holiday.Name = request.Name.Trim();
        holiday.Date = request.Date.Date;
        await _context.SaveChangesAsync();
        await _audit.LogAsync(userId.Value, companyId.Value, "UPDATE", "Holiday", holiday.Id);

        return Ok(ApiResponse<object>.SuccessResponse(new { holiday.Id, holiday.Name, holiday.Date }, "Holiday updated"));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var companyId = User.GetCompanyId();
        var userId = User.GetUserId();
        if (companyId == null || userId == null) return Unauthorized();

        var holiday = await _context.Holidays
            .FirstOrDefaultAsync(h => h.Id == id && h.CompanyId == companyId);

        if (holiday == null) return NotFound(ApiResponse<string>.FailResponse("Holiday not found"));

        holiday.IsActive = false;
        await _context.SaveChangesAsync();
        await _audit.LogAsync(userId.Value, companyId.Value, "DELETE", "Holiday", holiday.Id);

        return Ok(ApiResponse<string>.SuccessResponse("Holiday deleted"));
    }
}
