using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using lms_api.Data;
using lms_api.DTOs;

namespace lms_api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _context;

    public AdminController(AppDbContext context)
    {
        _context = context;
    }

    // ✅ GET EMPLOYEES
    [HttpGet("employees")]
    public async Task<IActionResult> GetEmployees()
    {
        var employees = await _context.Users
            .Where(u => u.Role != Models.Enums.UserRole.Admin)
            .Select(u => new {
                id = u.Id,
                name = u.FullName,
                email = u.Email,
                role = u.Role.ToString(),
                department = u.Department
            })
            .ToListAsync();

        return Ok(employees);
    }

    // ✅ DELETE EMPLOYEE
    [HttpDelete("employees/{id}")]
    public async Task<IActionResult> DeleteEmployee(Guid id)
    {
        var user = await _context.Users.FindAsync(id);

        if (user == null)
            return NotFound("Employee not found");

        _context.Users.Remove(user);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Employee deleted" });
    }

    [HttpPut("assign-manager")]
public async Task<IActionResult> AssignManager([FromBody] AssignManagerRequest request)
{
    var employee = await _context.Users
        .FirstOrDefaultAsync(u => u.Id == request.EmployeeId);

    if (employee == null)
        return NotFound("Employee not found");

    var manager = await _context.Users
        .FirstOrDefaultAsync(u => u.Id == request.ManagerId);

    if (manager == null)
        return NotFound("Manager not found");

    if (manager.Role != Models.Enums.UserRole.Manager)
        return BadRequest("Selected user is not a manager");

    employee.ManagerId = manager.Id;

    await _context.SaveChangesAsync();

    return Ok(new { message = "Manager assigned successfully" });
}

[HttpGet("managers")]
public async Task<IActionResult> GetManagers()
{
    var managers = await _context.Users
        .Where(u => u.Role == Models.Enums.UserRole.Manager)
        .Select(u => new {
            id = u.Id,
            name = u.FullName,
            email = u.Email,
            department = u.Department
        })
        .ToListAsync();

    return Ok(managers);
}

[HttpGet("manager-employees/{managerId}")]
public async Task<IActionResult> GetManagerEmployees(Guid managerId)
{
    var employees = await _context.Users
        .Where(u => u.ManagerId == managerId)
        .Select(u => new {
            id = u.Id,
            name = u.FullName,
            email = u.Email,
            department = u.Department
        })
        .ToListAsync();

    return Ok(employees);
}
}