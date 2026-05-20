using lms_api.Data;
using lms_api.Models;

namespace lms_api.Services;

public interface ILoginAttemptService
{
    Task<(bool Success, string? Error)> ValidateCredentialsAsync(User user, string password);
    Task RecordFailedAttemptAsync(User user);
    bool IsLockedOut(User user);
}

public class LoginAttemptService : ILoginAttemptService
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _configuration;

    public LoginAttemptService(AppDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
    }

    private int MaxAttempts => _configuration.GetValue("Security:MaxFailedLoginAttempts", 5);
    private int LockoutMinutes => _configuration.GetValue("Security:LockoutMinutes", 15);

    public bool IsLockedOut(User user) =>
        user.LockoutEndUtc.HasValue && user.LockoutEndUtc > DateTime.UtcNow;

    public async Task RecordFailedAttemptAsync(User user)
    {
        user.FailedLoginAttempts++;
        if (user.FailedLoginAttempts >= MaxAttempts)
        {
            user.LockoutEndUtc = DateTime.UtcNow.AddMinutes(LockoutMinutes);
        }

        await _context.SaveChangesAsync();
    }

    public async Task<(bool Success, string? Error)> ValidateCredentialsAsync(User user, string password)
    {
        if (IsLockedOut(user))
        {
            var remaining = user.LockoutEndUtc!.Value - DateTime.UtcNow;
            return (false, $"Account locked. Try again in {Math.Ceiling(remaining.TotalMinutes)} minutes.");
        }

        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            await RecordFailedAttemptAsync(user);
            return (false, "Invalid credentials");
        }

        user.FailedLoginAttempts = 0;
        user.LockoutEndUtc = null;
        await _context.SaveChangesAsync();
        return (true, null);
    }
}
