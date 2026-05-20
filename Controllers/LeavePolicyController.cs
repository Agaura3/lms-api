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
[Route("api/leave-policies")]
[Authorize(Roles = "Admin")]
public class LeavePolicyController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IAuditService _audit;

    public LeavePolicyController(AppDbContext context, IAuditService audit)
    {
        _context = context;
        _audit = audit;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var companyId = User.GetCompanyId();
        if (companyId == null) return Unauthorized();

        var policies = await _context.LeavePolicies
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId)
            .OrderBy(p => p.LeaveTypeName)
            .Select(p => new
            {
                p.Id,
                name = p.LeaveTypeName,
                leaves = p.MaxDaysPerYear,
                carryForward = p.CarryForwardLimit > 0,
                p.CarryForwardLimit,
                p.IsActive
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.SuccessResponse(policies));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateLeavePolicyRequest request)
    {
        var companyId = User.GetCompanyId();
        var userId = User.GetUserId();
        if (companyId == null || userId == null) return Unauthorized();

        var policy = new LeavePolicy
        {
            CompanyId = companyId.Value,
            LeaveTypeName = request.LeaveTypeName.Trim(),
            MaxDaysPerYear = request.MaxDaysPerYear,
            CarryForwardLimit = request.CarryForwardLimit
        };

        _context.LeavePolicies.Add(policy);
        await _context.SaveChangesAsync();
        await _audit.LogAsync(userId.Value, companyId.Value, "CREATE", "LeavePolicy", policy.Id);

        return Ok(ApiResponse<object>.SuccessResponse(new { policy.Id }, "Policy created"));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateLeavePolicyRequest request)
    {
        var companyId = User.GetCompanyId();
        var userId = User.GetUserId();
        if (companyId == null || userId == null) return Unauthorized();

        var policy = await _context.LeavePolicies
            .FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == companyId);

        if (policy == null) return NotFound(ApiResponse<string>.FailResponse("Policy not found"));

        policy.LeaveTypeName = request.LeaveTypeName.Trim();
        policy.MaxDaysPerYear = request.MaxDaysPerYear;
        policy.CarryForwardLimit = request.CarryForwardLimit;
        policy.IsActive = request.IsActive;

        await _context.SaveChangesAsync();
        await _audit.LogAsync(userId.Value, companyId.Value, "UPDATE", "LeavePolicy", policy.Id);

        return Ok(ApiResponse<string>.SuccessResponse("Policy updated"));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var companyId = User.GetCompanyId();
        var userId = User.GetUserId();
        if (companyId == null || userId == null) return Unauthorized();

        var policy = await _context.LeavePolicies
            .FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == companyId);

        if (policy == null) return NotFound(ApiResponse<string>.FailResponse("Policy not found"));

        policy.IsActive = false;
        await _context.SaveChangesAsync();
        await _audit.LogAsync(userId.Value, companyId.Value, "DELETE", "LeavePolicy", policy.Id);

        return Ok(ApiResponse<string>.SuccessResponse("Policy deleted"));
    }
}
