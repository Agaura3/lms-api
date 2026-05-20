using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using lms_api.Common;
using lms_api.Data;
using lms_api.DTOs;
using lms_api.Extensions;
using lms_api.Models.Enums;
using lms_api.Services;

namespace lms_api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IAuditService _audit;

    public AdminController(AppDbContext context, IAuditService audit)
    {
        _context = context;
        _audit = audit;
    }

    [HttpGet("employees")]
    public async Task<IActionResult> GetEmployees()
    {
        var companyId = User.GetCompanyId();
        if (companyId == null) return Unauthorized();

        var employees = await _context.Users
            .AsNoTracking()
            .Where(u => u.CompanyId == companyId && u.Role != UserRole.Admin)
            .Select(u => new
            {
                id = u.Id,
                name = u.FullName,
                email = u.Email,
                role = u.Role.ToString(),
                department = u.Department,
                managerId = u.ManagerId,
                managerName = _context.Users
                    .Where(m => m.Id == u.ManagerId)
                    .Select(m => m.FullName)
                    .FirstOrDefault()
            })
            .ToListAsync();

        return Ok(employees);
    }

    [HttpDelete("employees/{id:guid}")]
    public async Task<IActionResult> DeleteEmployee(Guid id)
    {
        var companyId = User.GetCompanyId();
        var userId = User.GetUserId();
        if (companyId == null || userId == null) return Unauthorized();

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == id && u.CompanyId == companyId && u.Role != UserRole.Admin);

        if (user == null)
            return NotFound(ApiResponse<string>.FailResponse("Employee not found"));

        _context.Users.Remove(user);
        await _context.SaveChangesAsync();
        await _audit.LogAsync(userId.Value, companyId.Value, "DELETE", "User", user.Id);

        return Ok(ApiResponse<string>.SuccessResponse("Employee deleted"));
    }

    [HttpPut("assign-manager")]
    public async Task<IActionResult> AssignManager([FromBody] AssignManagerRequest request)
    {
        var companyId = User.GetCompanyId();
        var userId = User.GetUserId();
        if (companyId == null || userId == null) return Unauthorized();

        var employee = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == request.EmployeeId && u.CompanyId == companyId);

        if (employee == null)
            return NotFound(ApiResponse<string>.FailResponse("Employee not found"));

        var manager = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == request.ManagerId && u.CompanyId == companyId);

        if (manager == null)
            return NotFound(ApiResponse<string>.FailResponse("Manager not found"));

        if (manager.Role != UserRole.Manager)
            return BadRequest(ApiResponse<string>.FailResponse("Selected user is not a manager"));

        employee.ManagerId = manager.Id;
        await _context.SaveChangesAsync();
        await _audit.LogAsync(userId.Value, companyId.Value, "ASSIGN_MANAGER", "User", employee.Id);

        return Ok(ApiResponse<string>.SuccessResponse("Manager assigned successfully"));
    }

    [HttpGet("managers")]
    public async Task<IActionResult> GetManagers()
    {
        var companyId = User.GetCompanyId();
        if (companyId == null) return Unauthorized();

        var managers = await _context.Users
            .AsNoTracking()
            .Where(u => u.CompanyId == companyId && u.Role == UserRole.Manager)
            .Select(u => new
            {
                id = u.Id,
                name = u.FullName,
                email = u.Email,
                department = u.Department
            })
            .ToListAsync();

        return Ok(managers);
    }

    [HttpGet("manager/{id:guid}")]
    public async Task<IActionResult> GetManager(Guid id)
    {
        var companyId = User.GetCompanyId();
        if (companyId == null) return Unauthorized();

        var manager = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == id && u.CompanyId == companyId && u.Role == UserRole.Manager)
            .Select(u => new
            {
                u.Id,
                name = u.FullName,
                u.Email,
                u.Department
            })
            .FirstOrDefaultAsync();

        if (manager == null)
            return NotFound(ApiResponse<string>.FailResponse("Manager not found"));

        return Ok(manager);
    }

    [HttpGet("manager-employees/{managerId:guid}")]
    public async Task<IActionResult> GetManagerEmployees(Guid managerId)
    {
        var companyId = User.GetCompanyId();
        if (companyId == null) return Unauthorized();

        var managerExists = await _context.Users.AnyAsync(u =>
            u.Id == managerId && u.CompanyId == companyId && u.Role == UserRole.Manager);

        if (!managerExists)
            return NotFound(ApiResponse<string>.FailResponse("Manager not found"));

        var employees = await _context.Users
            .AsNoTracking()
            .Where(u => u.CompanyId == companyId && u.ManagerId == managerId)
            .Select(u => new
            {
                u.Id,
                name = u.FullName,
                u.Email,
                u.Department
            })
            .ToListAsync();

        return Ok(employees);
    }
}
