using Microsoft.AspNetCore.SignalR;
using lms_api.Hubs;

namespace lms_api.Services;

public interface IDashboardBroadcastService
{
    Task NotifyCompanyDataChangedAsync(Guid companyId);
}

public class DashboardBroadcastService : IDashboardBroadcastService
{
    private readonly IHubContext<NotificationHub> _hubContext;

    public DashboardBroadcastService(IHubContext<NotificationHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task NotifyCompanyDataChangedAsync(Guid companyId) =>
        _hubContext.Clients
            .Group(CompanyGroup(companyId))
            .SendAsync("DataChanged", new { scope = "dashboard", at = DateTime.UtcNow });

    public static string CompanyGroup(Guid companyId) => $"company_{companyId}";
}
