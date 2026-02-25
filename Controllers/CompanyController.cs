using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using lms_api.Data;
using lms_api.Models;
using lms_api.DTOs;
using lms_api.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace lms_api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CompanyController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _config;

    public CompanyController(AppDbContext context, IConfiguration config)
    {
        _context = context;
        _config = config;
    }

    // 🔹 Register Company + First Admin
    [HttpPost("register")]
    public async Task<IActionResult> RegisterCompany([FromBody] RegisterCompanyRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ApiResponse<string>.FailResponse("Invalid request data."));

        if (await _context.Users.AnyAsync(u => u.Email == request.AdminEmail))
            return BadRequest(ApiResponse<string>.FailResponse("Email already exists."));

        var company = new Company
        {
            Id = Guid.NewGuid(),
            Name = request.CompanyName
        };

        _context.Companies.Add(company);
        await _context.SaveChangesAsync();

        var adminUser = new User
        {
            Id = Guid.NewGuid(),
            FullName = request.AdminName,
            Email = request.AdminEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = UserRole.Admin,
            CompanyId = company.Id
        };

        _context.Users.Add(adminUser);
        await _context.SaveChangesAsync();

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, adminUser.Id.ToString()),
            new Claim(ClaimTypes.Email, adminUser.Email),
            new Claim(ClaimTypes.Role, adminUser.Role.ToString()),
            new Claim("CompanyId", company.Id.ToString()) // 🔥 Standardized
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var tokenExpiryHours = _config.GetValue<int>("Jwt:ExpiryHours", 2);

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(tokenExpiryHours),
            signingCredentials: creds
        );

        var jwt = new JwtSecurityTokenHandler().WriteToken(token);

        return Ok(ApiResponse<object>.SuccessResponse(new
        {
            token = jwt,
            companyId = company.Id
        }, "Company registered successfully"));
    }

    // 🔹 Admin creates Employee
    [Authorize(Policy = "ApproveLeave")]
    [HttpPost("add-employee")]
    public async Task<IActionResult> AddEmployee([FromBody] RegisterEmployeeRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ApiResponse<string>.FailResponse("Invalid request data."));

        var companyIdClaim = User.FindFirst("CompanyId")?.Value;

        if (companyIdClaim == null)
            return Unauthorized(ApiResponse<string>.FailResponse("Invalid token."));

        var companyId = Guid.Parse(companyIdClaim);

        if (await _context.Users.AnyAsync(u => u.Email == request.Email))
            return BadRequest(ApiResponse<string>.FailResponse("Email already exists."));

        var employee = new User
        {
            Id = Guid.NewGuid(),
            FullName = request.Name,
            Email = request.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = UserRole.Employee,
            CompanyId = companyId,
            Department = request.Department
        };

        _context.Users.Add(employee);
        await _context.SaveChangesAsync();

        return Ok(ApiResponse<string>.SuccessResponse("Employee created successfully"));
    }

    // 🔹 Admin views all employees
    [Authorize(Policy = "ViewDashboard")]
    [HttpGet("employees")]
    public async Task<IActionResult> GetEmployees()
    {
        var companyIdClaim = User.FindFirst("CompanyId")?.Value;

        if (companyIdClaim == null)
            return Unauthorized(ApiResponse<string>.FailResponse("Invalid token."));

        var companyId = Guid.Parse(companyIdClaim);

        var employees = await _context.Users
            .Where(u => u.CompanyId == companyId && u.Role == UserRole.Employee)
            .Select(u => new
            {
                u.Id,
                u.FullName,
                u.Email,
                Role = u.Role.ToString()
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.SuccessResponse(employees, "Employees fetched successfully"));
    }
}