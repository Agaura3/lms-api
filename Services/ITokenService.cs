using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using lms_api.Data;
using lms_api.Models;

namespace lms_api.Services;

public interface ITokenService
{
    string GenerateAccessToken(User user);
    Task<(string RefreshToken, RefreshToken Entity)> CreateRefreshTokenAsync(User user);
    Task<User?> ValidateRefreshTokenAsync(string refreshToken);
    Task RevokeRefreshTokenAsync(RefreshToken token, string? replacedByHash = null);
}

public class TokenService : ITokenService
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _configuration;

    public TokenService(AppDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
    }

    public string GenerateAccessToken(User user)
    {
        var key = Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role.ToString()),
            new("CompanyId", user.CompanyId.ToString())
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(
                Convert.ToDouble(_configuration["Jwt:DurationInMinutes"] ?? "60")),
            Issuer = _configuration["Jwt:Issuer"],
            Audience = _configuration["Jwt:Audience"],
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
        };

        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(tokenDescriptor));
    }

    public async Task<(string RefreshToken, RefreshToken Entity)> CreateRefreshTokenAsync(User user)
    {
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var hash = BCrypt.Net.BCrypt.HashPassword(rawToken);

        var entity = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = hash,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };

        _context.RefreshTokens.Add(entity);
        await _context.SaveChangesAsync();

        return (rawToken, entity);
    }

    public async Task<User?> ValidateRefreshTokenAsync(string refreshToken)
    {
        var tokens = await _context.RefreshTokens
            .Include(t => t.User)
            .Where(t => t.RevokedAt == null && t.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(t => t.CreatedAt)
            .Take(50)
            .ToListAsync();

        var match = tokens.FirstOrDefault(t => BCrypt.Net.BCrypt.Verify(refreshToken, t.TokenHash));
        return match?.User;
    }

    public async Task RevokeRefreshTokenAsync(RefreshToken token, string? replacedByHash = null)
    {
        token.RevokedAt = DateTime.UtcNow;
        token.ReplacedByTokenHash = replacedByHash;
        await _context.SaveChangesAsync();
    }
}
