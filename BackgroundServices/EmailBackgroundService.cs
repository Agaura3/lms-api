using Microsoft.EntityFrameworkCore;
using lms_api.Data;
using lms_api.Models;
using lms_api.Services;

namespace lms_api.BackgroundServices;

public class EmailBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;

    public EmailBackgroundService(IServiceScopeFactory scopeFactory, IConfiguration config)
    {
        _scopeFactory = scopeFactory;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

            var maxRetry = int.Parse(_config["EmailSettings:MaxRetryAttempts"]!);
            var retentionDays = int.Parse(_config["EmailSettings:RetentionDays"]!);

            var pendingEmails = await db.EmailQueues
                .Where(e => e.Status == EmailStatus.Pending && e.RetryCount < maxRetry)
                .ToListAsync(stoppingToken);

            foreach (var email in pendingEmails)
            {
                try
                {
                    await emailService.SendEmailAsync(email.ToEmail, email.Subject, email.Body);

                    email.Status = EmailStatus.Sent;
                    email.SentAt = DateTime.UtcNow;
                }
                catch (Exception ex)
                {
                    email.RetryCount++;
                    email.ErrorMessage = ex.Message;

                    if (email.RetryCount >= maxRetry)
                        email.Status = EmailStatus.Failed;
                }
            }

            // Retention Cleanup
            var cutoffDate = DateTime.UtcNow.AddDays(-retentionDays);

            var oldEmails = await db.EmailQueues
                .Where(e => e.CreatedAt < cutoffDate)
                .ToListAsync(stoppingToken);

            db.EmailQueues.RemoveRange(oldEmails);

            await db.SaveChangesAsync(stoppingToken);

            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }
}