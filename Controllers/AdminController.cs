using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using lms_api.Data;

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
}