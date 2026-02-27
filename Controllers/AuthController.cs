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

    // 🔹 REGISTER (First Admin Setup)
// 🔹 REGISTER (First Admin Setup)
[HttpPost("register")]
public async Task<IActionResult> Register([FromBody] RegisterCompanyRequest request)
{
    var existingUser = await _context.Users
        .FirstOrDefaultAsync(u => u.Email == request.AdminEmail);

    if (existingUser != null)
        return BadRequest(ApiResponse<string>.FailResponse("Admin email already exists"));

    // Create Company
    var company = new Company
    {
        Id = Guid.NewGuid(),
        Name = request.CompanyName
    };

    _context.Companies.Add(company);

    var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);

    // Create Admin User
    var adminUser = new User
    {
        Id = Guid.NewGuid(),
        Email = request.AdminEmail,
        PasswordHash = passwordHash,
        Role = UserRole.Admin,
        CompanyId = company.Id
    };

    _context.Users.Add(adminUser);

    await _context.SaveChangesAsync();

    return Ok(ApiResponse<string>.SuccessResponse("Company and Admin registered successfully"));
}

// 🔹 REGISTER EMPLOYEE (Admin Only)
[Authorize(Roles = "Admin")]
[HttpPost("register-employee")]
public async Task<IActionResult> RegisterEmployee([FromBody] RegisterEmployeeRequest request)
{
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
        Role = UserRole.Employee,
        CompanyId = admin.CompanyId
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

    // 🔹 REFRESH TOKEN
    [HttpPost("refresh")]
    public async Task<IActionResult> RefreshToken([FromBody] string refreshToken)
    {
        if (string.IsNullOrEmpty(refreshToken))
            return BadRequest(ApiResponse<string>.FailResponse("Invalid refresh token"));

        var tokens = await _context.RefreshTokens
            .Include(rt => rt.User)
            .ToListAsync();

        var existingToken = tokens
            .FirstOrDefault(rt => BCrypt.Net.BCrypt.Verify(refreshToken, rt.TokenHash));

        if (existingToken == null)
            return Unauthorized(ApiResponse<string>.FailResponse("Invalid refresh token"));

        if (!existingToken.IsActive)
            return Unauthorized(ApiResponse<string>.FailResponse("Refresh token expired or revoked"));

        var user = existingToken.User;

        var newAccessToken = GenerateJwtToken(user);

        var newRefreshToken = Guid.NewGuid().ToString();
        var newRefreshTokenHash = BCrypt.Net.BCrypt.HashPassword(newRefreshToken);

        existingToken.RevokedAt = DateTime.UtcNow;
        existingToken.ReplacedByTokenHash = newRefreshTokenHash;

        var newTokenEntity = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = newRefreshTokenHash,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };

        _context.RefreshTokens.Add(newTokenEntity);
        await _context.SaveChangesAsync();

        return Ok(ApiResponse<object>.SuccessResponse(new
        {
            accessToken = newAccessToken,
            refreshToken = newRefreshToken
        }, "Token refreshed successfully"));
    }

    // 🔹 LOGOUT (Single Session)
    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] string refreshToken)
    {
        if (string.IsNullOrEmpty(refreshToken))
            return BadRequest(ApiResponse<string>.FailResponse("Invalid refresh token"));

        var tokens = await _context.RefreshTokens.ToListAsync();

        var existingToken = tokens
            .FirstOrDefault(rt => BCrypt.Net.BCrypt.Verify(refreshToken, rt.TokenHash));

        if (existingToken == null)
            return Unauthorized(ApiResponse<string>.FailResponse("Invalid refresh token"));

        existingToken.RevokedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(ApiResponse<string>.SuccessResponse("Logged out successfully"));
    }

    // 🔹 REVOKE ALL SESSIONS (Admin Only)
    [Authorize(Roles = "Admin")]
    [HttpPost("revoke-all/{userId}")]
    public async Task<IActionResult> RevokeAllTokens(Guid userId)
    {
        var tokens = await _context.RefreshTokens
            .Where(rt => rt.UserId == userId && rt.RevokedAt == null)
            .ToListAsync();

        if (!tokens.Any())
            return Ok(ApiResponse<string>.SuccessResponse("No active sessions found"));

        foreach (var token in tokens)
        {
            token.RevokedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();

        return Ok(ApiResponse<string>.SuccessResponse("All sessions revoked successfully"));
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
            new Claim("companyId", user.CompanyId.ToString())
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
}