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