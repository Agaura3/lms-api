using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authorization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using lms_api.Data;
using lms_api.Models;
using lms_api.DTOs;
using lms_api.Models.Enums;
using Microsoft.AspNetCore.RateLimiting;

namespace lms_api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _configuration;

    public AuthController(AppDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
    }

   // 🔹 REGISTER (Company + First Admin)
[HttpPost("register")]
public async Task<IActionResult> Register([FromBody] RegisterCompanyRequest request)
{
    var existingUser = await _context.Users
        .FirstOrDefaultAsync(u => u.Email == request.Email);

    if (existingUser != null)
        return BadRequest(ApiResponse<string>.FailResponse("Email already exists"));

    var existingCompany = await _context.Companies
        .FirstOrDefaultAsync(c => c.Name == request.CompanyName);

    if (existingCompany != null)
        return BadRequest(ApiResponse<string>.FailResponse("Company already exists"));

    var company = new Company
    {
        Id = Guid.NewGuid(),
        Name = request.CompanyName
    };

    _context.Companies.Add(company);

    var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);

    var adminUser = new User
    {
        Id = Guid.NewGuid(),
        Email = request.Email,
        PasswordHash = passwordHash,
        Role = UserRole.Admin,
        CompanyId = company.Id,
        FullName = request.Name
    };

    _context.Users.Add(adminUser);

    try
    {
        await _context.SaveChangesAsync();
    }
    catch (Exception ex)
    {
        return BadRequest(ApiResponse<string>.FailResponse(ex.InnerException?.Message ?? ex.Message));
    }

    return Ok(ApiResponse<string>.SuccessResponse("Company and Admin registered successfully"));
}
    // 🔹 REGISTER EMPLOYEE (Admin Only)
    [Authorize(Roles = "Admin")]
    [HttpPost("register-employee")]
    public async Task<IActionResult> RegisterEmployee([FromBody] RegisterEmployeeRequest request)
    {
        if (request.Role == UserRole.Admin)
{
            return BadRequest("Cannot create another admin");
}
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (userId == null)
            return Unauthorized(ApiResponse<string>.FailResponse("Invalid token"));

        var admin = await _context.Users
            .FirstOrDefaultAsync(u => u.Id.ToString() == userId);

        if (admin == null)
            return Unauthorized(ApiResponse<string>.FailResponse("Admin not found"));

        var existingUser = await _context.Users
            .FirstOrDefaultAsync(u => u.Email == request.Email);

        if (existingUser != null)
            return BadRequest(ApiResponse<string>.FailResponse("Email already exists"));

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);

        var employee = new User
        {
            Id = Guid.NewGuid(),
            Email = request.Email,
            PasswordHash = passwordHash,
            Role = request.Role,
            CompanyId = admin.CompanyId,
            FullName = request.Name,
            Department = request.Department
        };

        _context.Users.Add(employee);
        await _context.SaveChangesAsync();

        return Ok(ApiResponse<string>.SuccessResponse("Employee registered successfully"));
    }

    // 🔹 LOGIN
    [EnableRateLimiting("auth")]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Email == request.Email);

        if (user == null)
            return Unauthorized(ApiResponse<string>.FailResponse("Invalid credentials"));

        var isPasswordValid = BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash);

        if (!isPasswordValid)
            return Unauthorized(ApiResponse<string>.FailResponse("Invalid credentials"));

        var accessToken = GenerateJwtToken(user);

        var refreshToken = Guid.NewGuid().ToString();
        var refreshTokenHash = BCrypt.Net.BCrypt.HashPassword(refreshToken);

        var refreshTokenEntity = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refreshTokenHash,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };

        _context.RefreshTokens.Add(refreshTokenEntity);
        await _context.SaveChangesAsync();

        return Ok(ApiResponse<object>.SuccessResponse(new
        {
            accessToken,
            refreshToken,
            role = user.Role.ToString()
        }, "Login successful"));
    }

    // 🔹 TOKEN GENERATOR
    private string GenerateJwtToken(User user)
    {
        var key = Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!);

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim("CompanyId", user.CompanyId.ToString())
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(
                Convert.ToDouble(_configuration["Jwt:DurationInMinutes"] ?? "15")
            ),
            Issuer = _configuration["Jwt:Issuer"],
            Audience = _configuration["Jwt:Audience"],
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature
            )
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);

        return tokenHandler.WriteToken(token);
    }

  [HttpPost("forgot-password")]
public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
{
    var user = await _context.Users
        .FirstOrDefaultAsync(u => u.Email == request.Email);

    if (user == null)
        return Ok(ApiResponse<string>.SuccessResponse("If email exists, reset link will be sent"));

    var resetToken = Guid.NewGuid().ToString();

    var resetEntity = new PasswordResetToken
    {
        UserId = user.Id,
        Token = resetToken,
        ExpiresAt = DateTime.UtcNow.AddHours(1)
    };

    _context.PasswordResetTokens.Add(resetEntity);
    await _context.SaveChangesAsync();

    var resetLink = $"https://lmsorbit.netlify.app/reset-password?token={resetToken}";

    var email = new EmailQueue
    {
        ToEmail = user.Email,
        Subject = "Reset Your Password",
        Body = $"Click this link to reset your password: {resetLink}",
        Status = EmailStatus.Pending,
        CreatedAt = DateTime.UtcNow
    };

    _context.EmailQueues.Add(email);
    await _context.SaveChangesAsync();

    return Ok(ApiResponse<string>.SuccessResponse("Reset link sent"));
}
}