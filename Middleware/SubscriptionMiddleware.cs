using Microsoft.EntityFrameworkCore;
using lms_api.Data;

namespace lms_api.Middleware;

public class SubscriptionMiddleware
{
    private readonly RequestDelegate _next;

    public SubscriptionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, AppDbContext db)
    {
        var companyId = context.User?.FindFirst("CompanyId")?.Value;

        if (!string.IsNullOrEmpty(companyId))
        {
            var subscription = await db.CompanySubscriptions
                .Include(s => s.Plan)
                .FirstOrDefaultAsync(s => s.CompanyId.ToString() == companyId && s.IsActive);

            if (subscription == null || subscription.EndDate < DateTime.UtcNow)
            {
                context.Response.StatusCode = 403;
                await context.Response.WriteAsync("Subscription expired.");
                return;
            }
        }

        await _next(context);
    }
}