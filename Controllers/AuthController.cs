using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using lms_api.Data;
using lms_api.Models;
using lms_api.DTOs;
using lms_api.Models.Enums;
using Microsoft.AspNetCore.RateLimiting;
using lms_api.Common;
using lms_api.Services;
using lms_api.Extensions;

namespace lms_api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ITokenService _tokenService;
    private readonly IConfiguration _configuration;
    private readonly IAuditService _audit;
    private readonly ILoginAttemptService _loginAttempts;

    public AuthController(
        AppDbContext context,
        ITokenService tokenService,
        IConfiguration configuration,
        IAuditService audit,
        ILoginAttemptService loginAttempts)
    {
        _context = context;
        _tokenService = tokenService;
        _configuration = configuration;
        _audit = audit;
        _loginAttempts = loginAttempts;
    }

    [HttpPost("register")]
  [EnableRateLimiting("auth")]
    public async Task<IActionResult> Register([FromBody] RegisterCompanyRequest request)
    {
        var existingUser = await _context.Users
            .AnyAsync(u => u.Email == request.Email);

        if (existingUser)
            return BadRequest(ApiResponse<string>.FailResponse("Email already exists"));

        var existingCompany = await _context.Companies
            .AnyAsync(c => c.Name == request.CompanyName);

        if (existingCompany)
            return BadRequest(ApiResponse<string>.FailResponse("Company already exists"));

        var company = new Company
        {
            Id = Guid.NewGuid(),
            Name = request.CompanyName
        };

        _context.Companies.Add(company);

        var adminUser = new User
        {
            Id = Guid.NewGuid(),
            Email = request.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = UserRole.Admin,
            CompanyId = company.Id,
            FullName = request.Name
        };

        _context.Users.Add(adminUser);
        _context.CompanySettings.Add(new CompanySettings { CompanyId = company.Id });

        await _context.SaveChangesAsync();
        await _audit.LogAsync(adminUser.Id, company.Id, "REGISTER", "Company", company.Id);

        return Ok(ApiResponse<string>.SuccessResponse("Company and Admin registered successfully"));
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("register-employee")]
    public async Task<IActionResult> RegisterEmployee([FromBody] RegisterEmployeeRequest request)
    {
        if (request.Role == UserRole.Admin)
            return BadRequest(ApiResponse<string>.FailResponse("Cannot create another admin"));

        var adminId = User.GetUserId();
        var companyId = User.GetCompanyId();
        if (adminId == null || companyId == null)
            return Unauthorized(ApiResponse<string>.FailResponse("Invalid token"));

        if (await _context.Users.AnyAsync(u => u.Email == request.Email))
            return BadRequest(ApiResponse<string>.FailResponse("Email already exists"));

        var employee = new User
        {
            Id = Guid.NewGuid(),
            Email = request.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = request.Role,
            CompanyId = companyId.Value,
            FullName = request.Name,
            Department = request.Department
        };

        _context.Users.Add(employee);
        await _context.SaveChangesAsync();
        await _audit.LogAsync(adminId.Value, companyId.Value, "CREATE", "User", employee.Id);

        return Ok(ApiResponse<string>.SuccessResponse("Employee registered successfully"));
    }

    [EnableRateLimiting("auth")]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Email == request.Email);

        if (user == null)
            return Unauthorized(ApiResponse<string>.FailResponse("Invalid credentials"));

        var (success, error) = await _loginAttempts.ValidateCredentialsAsync(user, request.Password);
        if (!success)
            return Unauthorized(ApiResponse<string>.FailResponse(error ?? "Invalid credentials"));

        var accessToken = _tokenService.GenerateAccessToken(user);
        var (refreshToken, _) = await _tokenService.CreateRefreshTokenAsync(user);

        return Ok(ApiResponse<object>.SuccessResponse(new
        {
            accessToken,
            refreshToken,
            role = user.Role.ToString()
        }, "Login successful"));
    }

    [EnableRateLimiting("auth")]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request)
    {
        var user = await _tokenService.ValidateRefreshTokenAsync(request.RefreshToken);
        if (user == null)
            return Unauthorized(ApiResponse<string>.FailResponse("Invalid or expired refresh token"));

        var existingTokens = await _context.RefreshTokens
            .Where(t => t.UserId == user.Id && t.RevokedAt == null && t.ExpiresAt > DateTime.UtcNow)
            .ToListAsync();

        var matched = existingTokens.FirstOrDefault(t =>
            BCrypt.Net.BCrypt.Verify(request.RefreshToken, t.TokenHash));

        if (matched == null)
            return Unauthorized(ApiResponse<string>.FailResponse("Invalid refresh token"));

        var accessToken = _tokenService.GenerateAccessToken(user);
        var (newRefresh, newEntity) = await _tokenService.CreateRefreshTokenAsync(user);
        await _tokenService.RevokeRefreshTokenAsync(matched, newEntity.TokenHash);

        return Ok(ApiResponse<object>.SuccessResponse(new
        {
            accessToken,
            refreshToken = newRefresh,
            role = user.Role.ToString()
        }, "Token refreshed"));
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var userId = User.GetUserId();
        if (userId == null) return Unauthorized();

        var tokens = await _context.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync();

        foreach (var token in tokens)
            token.RevokedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return Ok(ApiResponse<string>.SuccessResponse("Logged out"));
    }

    [EnableRateLimiting("auth")]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Email == request.Email);

        if (user == null)
            return Ok(ApiResponse<string>.SuccessResponse("If email exists, reset link will be sent"));

        var resetToken = Guid.NewGuid().ToString("N");

        _context.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = user.Id,
            Token = resetToken,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        });

        var frontendUrl = _configuration["App:FrontendUrl"] ?? "http://localhost:4200";
        var resetLink = $"{frontendUrl.TrimEnd('/')}/reset-password?token={resetToken}";

        _context.EmailQueues.Add(new EmailQueue
        {
            ToEmail = user.Email,
            Subject = "Reset Your Password",
            Body = $"Click this link to reset your password: {resetLink}",
            Status = EmailStatus.Pending,
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();
        return Ok(ApiResponse<string>.SuccessResponse("Reset link sent"));
    }

    [EnableRateLimiting("auth")]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        var tokenEntity = await _context.PasswordResetTokens
            .FirstOrDefaultAsync(t => t.Token == request.Token && t.ExpiresAt > DateTime.UtcNow);

        if (tokenEntity == null)
            return BadRequest(ApiResponse<string>.FailResponse("Invalid or expired token"));

        var user = await _context.Users.FindAsync(tokenEntity.UserId);
        if (user == null)
            return BadRequest(ApiResponse<string>.FailResponse("User not found"));

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        _context.PasswordResetTokens.Remove(tokenEntity);
        await _context.SaveChangesAsync();

        return Ok(ApiResponse<string>.SuccessResponse("Password reset successful"));
    }
}
